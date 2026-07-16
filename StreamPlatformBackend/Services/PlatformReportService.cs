using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models.Staff;

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
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(15);
        private const int MaxReportsPerHour = 20;

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

            if (targetType == ReportTargetTypes.Channel && streamerId == null)
                streamerId = targetUserId;

            if (targetUserId == reporterUserId)
                return (null, "Нельзя пожаловаться на себя");

            if (targetUserId != null &&
                !await _context.Users.AnyAsync(u => u.Id == targetUserId.Value))
                return (null, "Пользователь не найден");

            if (streamerId != null &&
                !await _context.Users.AnyAsync(u => u.Id == streamerId.Value))
                return (null, "Канал не найден");

            var hourAgo = DateTime.UtcNow.AddHours(-1);
            var recentCount = await _context.PlatformReports
                .CountAsync(r => r.ReporterUserId == reporterUserId && r.CreatedAt >= hourAgo);
            if (recentCount >= MaxReportsPerHour)
                return (null, "Слишком много жалоб. Попробуйте позже");

            var dupSince = DateTime.UtcNow - DuplicateWindow;
            var duplicate = await _context.PlatformReports.AnyAsync(r =>
                r.ReporterUserId == reporterUserId
                && r.TargetType == targetType
                && r.TargetUserId == targetUserId
                && r.MessageId == messageId
                && r.CreatedAt >= dupSince
                && r.Status != ReportStatuses.Rejected);

            if (duplicate)
                return (null, "Вы уже отправляли похожую жалобу недавно");

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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PlatformReports.Add(report);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Report {ReportId} created by {ReporterId} type={Type} target={TargetUserId}",
                report.Id, reporterUserId, targetType, targetUserId);

            return (await MapAsync(report.Id), null);
        }

        public async Task<List<PlatformReportDto>> ListAsync(string? status = null, int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            var query = _context.PlatformReports.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToLowerInvariant();
                if (ReportStatuses.IsKnown(normalized))
                    query = query.Where(r => r.Status == normalized);
            }

            var items = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(take)
                .Include(r => r.Reporter)
                .Include(r => r.TargetUser)
                .Include(r => r.Streamer)
                .Include(r => r.Assignee)
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

                var (sanction, sanctionError) = await _sanctions.IssueAsync(actorUserId, dto.Sanction);
                if (sanctionError != null)
                    return (null, sanctionError);

                report.LinkedSanctionId = sanction?.Id;
                if (report.Status is ReportStatuses.New or ReportStatuses.InProgress)
                {
                    report.Status = ReportStatuses.Resolved;
                    report.ResolvedAt = DateTime.UtcNow;
                    report.AssigneeUserId ??= actorUserId;
                }
            }

            report.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

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

        private async Task<PlatformReportDto?> MapAsync(long id)
        {
            var report = await _context.PlatformReports.AsNoTracking()
                .Include(r => r.Reporter)
                .Include(r => r.TargetUser)
                .Include(r => r.Streamer)
                .Include(r => r.Assignee)
                .FirstOrDefaultAsync(r => r.Id == id);

            return report == null ? null : Map(report);
        }

        private static PlatformReportDto Map(PlatformReport r) => new()
        {
            Id = r.Id,
            ReporterUserId = r.ReporterUserId,
            ReporterNickname = r.Reporter?.Nickname,
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
