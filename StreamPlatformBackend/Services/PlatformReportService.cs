using Microsoft.EntityFrameworkCore;
using Npgsql;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models.Staff;
using System.Security.Cryptography;
using System.Text;

namespace StreamPlatformBackend.Services
{
    public interface IPlatformReportService
    {
        Task<(PlatformReportDto? report, string? error)> CreateAsync(int reporterUserId, CreatePlatformReportDto dto);
        Task<List<PlatformReportDto>> ListAsync(string? status = null, int take = 50);
        Task<PlatformReportDto?> GetByIdAsync(long id);
        Task<(PlatformReportDto? report, string? error)> UpdateAsync(int actorUserId, long id, UpdatePlatformReportDto dto);
    }

    public class PlatformReportService : IPlatformReportService
    {
        private const int MaxReportsPerHour = 20;

        private static readonly string[] OpenStatuses =
        {
            ReportStatuses.New,
            ReportStatuses.InProgress
        };

        private readonly AppDbContext _context;
        private readonly IPlatformSanctionService _sanctions;
        private readonly IStaffAuditService _audit;
        private readonly ILogger<PlatformReportService> _logger;

        public PlatformReportService(
            AppDbContext context,
            IPlatformSanctionService sanctions,
            IStaffAuditService audit,
            ILogger<PlatformReportService> logger)
        {
            _context = context;
            _sanctions = sanctions;
            _audit = audit;
            _logger = logger;
        }

        public async Task<(PlatformReportDto? report, string? error)> CreateAsync(int reporterUserId, CreatePlatformReportDto dto)
        {
            var targetType = (dto.TargetType ?? string.Empty).Trim().ToLowerInvariant();
            if (!ReportTargetTypes.IsKnown(targetType))
                return (null, "Неизвестный тип жалобы");

            var reason = (dto.Reason ?? string.Empty).Trim().ToLowerInvariant();
            if (!ReportReasons.IsKnown(reason))
                return (null, "Неизвестная причина");

            var details = string.IsNullOrWhiteSpace(dto.Details) ? null : dto.Details.Trim();
            if (details is { Length: > 500 })
                return (null, "Комментарий слишком длинный");

            var messageId = string.IsNullOrWhiteSpace(dto.MessageId) ? null : dto.MessageId.Trim();
            var messageSnapshot = string.IsNullOrWhiteSpace(dto.MessageSnapshot)
                ? null
                : dto.MessageSnapshot.Trim();
            if (messageSnapshot is { Length: > 1000 })
                messageSnapshot = messageSnapshot[..1000];

            int? targetUserId = dto.TargetUserId is > 0 ? dto.TargetUserId : null;
            int? streamerId = dto.StreamerId is > 0 ? dto.StreamerId : null;
            int? streamId = dto.StreamId is > 0 ? dto.StreamId : null;

            if (targetType is ReportTargetTypes.Message)
            {
                if (string.IsNullOrEmpty(messageId))
                    return (null, "Не указан идентификатор сообщения");
                if (targetUserId == null)
                    return (null, "Не указан автор сообщения");
            }

            if (targetType is ReportTargetTypes.User or ReportTargetTypes.Channel or ReportTargetTypes.Stream)
            {
                if (targetUserId == null)
                    return (null, "Не указан пользователь");
            }

            if (targetType is ReportTargetTypes.Channel or ReportTargetTypes.Stream)
            {
                streamerId ??= targetUserId;
            }

            if (targetType == ReportTargetTypes.Stream && streamId == null)
                return (null, "Не указан стрим");

            if (targetUserId == reporterUserId)
                return (null, "Нельзя пожаловаться на себя");

            if (targetUserId != null &&
                !await _context.Users.AnyAsync(u => u.Id == targetUserId.Value))
                return (null, "Пользователь не найден");

            if (streamerId != null &&
                !await _context.Users.AnyAsync(u => u.Id == streamerId.Value))
                return (null, "Канал не найден");

            var hourAgo = DateTime.UtcNow.AddHours(-1);
            var recentCount = await _context.PlatformReportReporters
                .CountAsync(r => r.ReporterUserId == reporterUserId && r.CreatedAt >= hourAgo);
            if (recentCount >= MaxReportsPerHour)
                return (null, "Слишком много жалоб. Попробуйте позже");

            // Serialize create/attach for the same case fingerprint so parallel submits
            // cannot open duplicate open cases or 500 on the unique reporter index.
            await using var tx = await _context.Database.BeginTransactionAsync();
            var lockKey = CaseLockKey(targetType, reason, targetUserId, streamId, messageId);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})");

            try
            {
                var existingCase = await FindOpenCaseAsync(targetType, reason, targetUserId, streamId, messageId);

                if (existingCase != null)
                {
                    var alreadyJoined = await _context.PlatformReportReporters.AnyAsync(r =>
                        r.ReportId == existingCase.Id && r.ReporterUserId == reporterUserId);
                    if (alreadyJoined)
                    {
                        await tx.RollbackAsync();
                        return (null, "Вы уже отправляли похожую жалобу по этому случаю");
                    }

                    _context.PlatformReportReporters.Add(new PlatformReportReporter
                    {
                        ReportId = existingCase.Id,
                        ReporterUserId = reporterUserId,
                        Details = details,
                        CreatedAt = DateTime.UtcNow
                    });

                    existingCase.UpdatedAt = DateTime.UtcNow;
                    if (string.IsNullOrEmpty(existingCase.MessageSnapshot) && !string.IsNullOrEmpty(messageSnapshot))
                        existingCase.MessageSnapshot = messageSnapshot;

                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                    {
                        await tx.RollbackAsync();
                        return (null, "Вы уже отправляли похожую жалобу по этому случаю");
                    }

                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "Report case {ReportId} +1 reporter {ReporterId} (now attached)",
                        existingCase.Id, reporterUserId);

                    return (await MapAsync(existingCase.Id), null);
                }

                var now = DateTime.UtcNow;
                var report = new PlatformReport
                {
                    ReporterUserId = reporterUserId,
                    TargetType = targetType,
                    TargetUserId = targetUserId,
                    MessageId = messageId,
                    MessageSnapshot = messageSnapshot,
                    StreamerId = streamerId,
                    StreamId = streamId,
                    Reason = reason,
                    Details = details,
                    Status = ReportStatuses.New,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // Single SaveChanges: case + first reporter (no orphan case without reporter).
                _context.PlatformReports.Add(report);
                _context.PlatformReportReporters.Add(new PlatformReportReporter
                {
                    Report = report,
                    ReporterUserId = reporterUserId,
                    Details = details,
                    CreatedAt = now
                });

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    await tx.RollbackAsync();
                    return (null, "Вы уже отправляли похожую жалобу по этому случаю");
                }

                await tx.CommitAsync();

                _logger.LogInformation(
                    "Report case {ReportId} opened by {ReporterId} type={Type} target={TargetUserId}",
                    report.Id, reporterUserId, targetType, targetUserId);

                return (await MapAsync(report.Id), null);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<PlatformReportDto>> ListAsync(string? status = null, int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            var query = _context.PlatformReports.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToLowerInvariant();
                if (normalized == ReportStatuses.Open)
                    query = query.Where(r => ReportStatuses.OpenStatuses.Contains(r.Status));
                else if (ReportStatuses.IsKnown(normalized))
                    query = query.Where(r => r.Status == normalized);
            }

            var items = await query
                .OrderByDescending(r => r.UpdatedAt)
                .ThenByDescending(r => r.CreatedAt)
                .Take(take)
                .Include(r => r.Reporter)
                .Include(r => r.TargetUser)
                .Include(r => r.Streamer)
                .Include(r => r.Assignee)
                .Include(r => r.Reporters)
                    .ThenInclude(x => x.Reporter)
                .ToListAsync();

            return items.Select(Map).ToList();
        }

        public async Task<PlatformReportDto?> GetByIdAsync(long id) => await MapAsync(id);

        public async Task<(PlatformReportDto? report, string? error)> UpdateAsync(
            int actorUserId,
            long id,
            UpdatePlatformReportDto dto)
        {
            var report = await _context.PlatformReports.FirstOrDefaultAsync(r => r.Id == id);
            if (report == null)
                return (null, "Жалоба не найдена");

            if (dto.AssignToMe)
                report.AssigneeUserId = actorUserId;

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var status = dto.Status.Trim().ToLowerInvariant();
                if (!ReportStatuses.IsKnown(status))
                    return (null, "Неизвестный статус");

                report.Status = status;
                if (status is ReportStatuses.Resolved or ReportStatuses.Rejected)
                {
                    report.ResolvedAt = DateTime.UtcNow;
                    if (report.AssigneeUserId == null)
                        report.AssigneeUserId = actorUserId;
                }
                else if (status == ReportStatuses.InProgress && report.AssigneeUserId == null)
                {
                    report.AssigneeUserId = actorUserId;
                }
            }

            if (dto.ResolutionNote != null)
            {
                var note = dto.ResolutionNote.Trim();
                if (note.Length > 500)
                    return (null, "Комментарий резолюции слишком длинный");
                report.ResolutionNote = string.IsNullOrEmpty(note) ? null : note;
            }

            if (dto.Sanction != null)
            {
                if (dto.Sanction.TargetUserId <= 0 && report.TargetUserId.HasValue)
                    dto.Sanction.TargetUserId = report.TargetUserId.Value;

                if (dto.Sanction.TargetUserId <= 0)
                    return (null, "Нельзя выдать санкцию без целевого пользователя");

                // Same DbContext: keep sanction insert + LinkedSanctionId in one transaction.
                await using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    var (sanction, sanctionError) = await _sanctions.IssueAsync(actorUserId, dto.Sanction);
                    if (sanctionError != null)
                    {
                        await tx.RollbackAsync();
                        return (null, sanctionError);
                    }

                    report.LinkedSanctionId = sanction?.Id;
                    if (report.Status is ReportStatuses.New or ReportStatuses.InProgress)
                    {
                        report.Status = ReportStatuses.Resolved;
                        report.ResolvedAt = DateTime.UtcNow;
                        report.AssigneeUserId ??= actorUserId;
                    }

                    report.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
            else
            {
                report.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            if (report.Status is ReportStatuses.Resolved or ReportStatuses.Rejected)
            {
                await _audit.WriteAsync(
                    actorUserId,
                    StaffAuditActions.ReportResolved,
                    targetUserId: report.TargetUserId,
                    entityType: "PlatformReport",
                    entityId: report.Id.ToString(),
                    details: $"{report.Status}: {report.ResolutionNote}");
            }

            return (await MapAsync(report.Id), null);
        }

        private static long CaseLockKey(
            string targetType,
            string reason,
            int? targetUserId,
            int? streamId,
            string? messageId)
        {
            var raw = $"{targetType}|{reason}|{targetUserId?.ToString() ?? "-"}|{streamId?.ToString() ?? "-"}|{messageId ?? ""}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToInt64(hash, 0);
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private async Task<PlatformReport?> FindOpenCaseAsync(
            string targetType,
            string reason,
            int? targetUserId,
            int? streamId,
            string? messageId)
        {
            var query = _context.PlatformReports
                .Where(r => OpenStatuses.Contains(r.Status)
                            && r.TargetType == targetType
                            && r.Reason == reason
                            && r.TargetUserId == targetUserId);

            if (targetType == ReportTargetTypes.Message)
                query = query.Where(r => r.MessageId == messageId);
            else if (targetType == ReportTargetTypes.Stream)
                query = query.Where(r => r.StreamId == streamId);
            else
                query = query.Where(r => r.MessageId == null);

            return await query
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        private async Task<PlatformReportDto?> MapAsync(long id)
        {
            var report = await _context.PlatformReports.AsNoTracking()
                .Include(r => r.Reporter)
                .Include(r => r.TargetUser)
                .Include(r => r.Streamer)
                .Include(r => r.Assignee)
                .Include(r => r.Reporters)
                    .ThenInclude(x => x.Reporter)
                .FirstOrDefaultAsync(r => r.Id == id);

            return report == null ? null : Map(report);
        }

        private static PlatformReportDto Map(PlatformReport r)
        {
            var reporters = (r.Reporters ?? Array.Empty<PlatformReportReporter>())
                .OrderBy(x => x.CreatedAt)
                .Select(x => new PlatformReportReporterDto
                {
                    Id = x.Id,
                    ReporterUserId = x.ReporterUserId,
                    ReporterNickname = x.Reporter?.Nickname,
                    Details = x.Details,
                    CreatedAt = x.CreatedAt
                })
                .ToList();

            if (reporters.Count == 0)
            {
                reporters.Add(new PlatformReportReporterDto
                {
                    Id = 0,
                    ReporterUserId = r.ReporterUserId,
                    ReporterNickname = r.Reporter?.Nickname,
                    Details = r.Details,
                    CreatedAt = r.CreatedAt
                });
            }

            return new PlatformReportDto
            {
                Id = r.Id,
                ReporterUserId = r.ReporterUserId,
                ReporterNickname = r.Reporter?.Nickname,
                ReporterCount = reporters.Count,
                Reporters = reporters,
                TargetType = r.TargetType,
                TargetUserId = r.TargetUserId,
                TargetNickname = r.TargetUser?.Nickname,
                MessageId = r.MessageId,
                MessageSnapshot = r.MessageSnapshot,
                StreamerId = r.StreamerId,
                StreamerNickname = r.Streamer?.Nickname,
                StreamId = r.StreamId,
                Reason = r.Reason,
                Details = r.Details,
                Status = r.Status,
                AssigneeUserId = r.AssigneeUserId,
                AssigneeNickname = r.Assignee?.Nickname,
                ResolutionNote = r.ResolutionNote,
                LinkedSanctionId = r.LinkedSanctionId,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                ResolvedAt = r.ResolvedAt
            };
        }
    }
}
