using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Staff;
using StreamPlatformBackend.Services.NotificationService;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{
    public interface IPlatformAppealService
    {
        Task<(PlatformAppealDto? appeal, string? error)> CreateAsync(int userId, CreatePlatformAppealDto dto);
        Task<List<PlatformAppealDto>> GetMineAsync(int userId);
        Task<List<PlatformAppealDto>> GetStaffAsync(string? status, int take = 50);
        Task<(PlatformAppealDto? appeal, string? error)> ReviewAsync(int staffUserId, long appealId, UpdatePlatformAppealDto dto);
    }

    public class PlatformAppealService : IPlatformAppealService
    {
        private readonly AppDbContext _context;
        private readonly IPlatformSanctionService _sanctions;
        private readonly IStaffAuditService _audit;
        private readonly INotificationRepository _notifications;
        private readonly INotificationSender _notificationSender;
        private readonly ILogger<PlatformAppealService> _logger;

        public PlatformAppealService(
            AppDbContext context,
            IPlatformSanctionService sanctions,
            IStaffAuditService audit,
            INotificationRepository notifications,
            INotificationSender notificationSender,
            ILogger<PlatformAppealService> logger)
        {
            _context = context;
            _sanctions = sanctions;
            _audit = audit;
            _notifications = notifications;
            _notificationSender = notificationSender;
            _logger = logger;
        }

        public async Task<(PlatformAppealDto? appeal, string? error)> CreateAsync(int userId, CreatePlatformAppealDto dto)
        {
            var message = (dto.Message ?? string.Empty).Trim();
            if (message.Length < 10)
                return (null, "Опишите причину апелляции (минимум 10 символов)");
            if (message.Length > 2000)
                return (null, "Текст слишком длинный");

            await _sanctions.ExpireOverdueAsync();

            var sanction = await _context.PlatformSanctions.FirstOrDefaultAsync(s => s.Id == dto.SanctionId);
            if (sanction == null)
                return (null, "Санкция не найдена");
            if (sanction.TargetUserId != userId)
                return (null, "Это не ваша санкция");
            if (sanction.Status != PlatformSanctionStatuses.Active)
                return (null, "Апелляцию можно подать только на активную санкцию");
            if (sanction.Type == PlatformSanctionTypes.Warning)
                return (null, "На предупреждение апелляция не нужна");

            var hasOpen = await _context.PlatformAppeals.AnyAsync(a =>
                a.SanctionId == sanction.Id &&
                AppealStatuses.OpenLike.Contains(a.Status));
            if (hasOpen)
                return (null, "По этой санкции уже есть открытая апелляция");

            var appeal = new PlatformAppeal
            {
                SanctionId = sanction.Id,
                UserId = userId,
                Message = message,
                Status = AppealStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PlatformAppeals.Add(appeal);
            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                userId,
                StaffAuditActions.AppealCreated,
                targetUserId: userId,
                entityType: "PlatformAppeal",
                entityId: appeal.Id.ToString(),
                details: $"sanction #{sanction.Id} ({sanction.Type})");

            _logger.LogInformation("Appeal {AppealId} created by user {UserId} for sanction {SanctionId}",
                appeal.Id, userId, sanction.Id);

            return (await MapAsync(appeal.Id), null);
        }

        public async Task<List<PlatformAppealDto>> GetMineAsync(int userId)
        {
            var items = await _context.PlatformAppeals.AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(50)
                .Include(a => a.Sanction)
                .Include(a => a.User)
                .Include(a => a.ReviewedByUser)
                .ToListAsync();

            return items.Select(Map).ToList();
        }

        public async Task<List<PlatformAppealDto>> GetStaffAsync(string? status, int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            var query = _context.PlatformAppeals.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim().ToLowerInvariant();
                if (AppealStatuses.IsKnown(s))
                    query = query.Where(a => a.Status == s);
            }

            var items = await query
                .OrderByDescending(a => a.CreatedAt)
                .Take(take)
                .Include(a => a.Sanction)
                .Include(a => a.User)
                .Include(a => a.ReviewedByUser)
                .ToListAsync();

            return items.Select(Map).ToList();
        }

        public async Task<(PlatformAppealDto? appeal, string? error)> ReviewAsync(
            int staffUserId,
            long appealId,
            UpdatePlatformAppealDto dto)
        {
            var appeal = await _context.PlatformAppeals
                .Include(a => a.Sanction)
                .FirstOrDefaultAsync(a => a.Id == appealId);
            if (appeal == null)
                return (null, "Апелляция не найдена");

            if (!AppealStatuses.OpenLike.Contains(appeal.Status) &&
                string.IsNullOrWhiteSpace(dto.Status))
            {
                return (null, "Апелляция уже закрыта");
            }

            if (!string.IsNullOrWhiteSpace(dto.StaffNote))
            {
                var note = dto.StaffNote.Trim();
                if (note.Length > 500)
                    return (null, "Заметка слишком длинная");
                appeal.StaffNote = note;
            }

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var status = dto.Status.Trim().ToLowerInvariant();
                if (!AppealStatuses.IsKnown(status))
                    return (null, "Неизвестный статус");

                if (status is AppealStatuses.Approved or AppealStatuses.Rejected)
                {
                    appeal.Status = status;
                    appeal.ResolvedAt = DateTime.UtcNow;
                    appeal.ReviewedByUserId = staffUserId;

                    if (status == AppealStatuses.Approved && dto.RevokeSanction &&
                        appeal.Sanction != null &&
                        appeal.Sanction.Status == PlatformSanctionStatuses.Active)
                    {
                        var (_, revokeError) = await _sanctions.RevokeAsync(
                            staffUserId,
                            appeal.SanctionId,
                            appeal.StaffNote ?? "Апелляция одобрена");
                        if (revokeError != null)
                            return (null, revokeError);
                    }

                    await NotifyUserAsync(
                        appeal.UserId,
                        appeal.Id,
                        status == AppealStatuses.Approved
                            ? "Апелляция одобрена"
                            : "Апелляция отклонена",
                        appeal.StaffNote);
                }
                else
                {
                    appeal.Status = status;
                    appeal.ReviewedByUserId ??= staffUserId;
                }
            }

            appeal.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                staffUserId,
                StaffAuditActions.AppealReviewed,
                targetUserId: appeal.UserId,
                entityType: "PlatformAppeal",
                entityId: appeal.Id.ToString(),
                details: $"{appeal.Status}: {appeal.StaffNote}");

            return (await MapAsync(appealId), null);
        }

        private async Task NotifyUserAsync(int userId, long appealId, string title, string? note)
        {
            var notification = new NotificationModel
            {
                UserId = userId,
                Type = NotificationType.PlatformAppeal,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    AppealId = appealId,
                    Message = string.IsNullOrWhiteSpace(note) ? title : $"{title}: {note}",
                    Title = title
                }),
                CreatedAt = DateTime.UtcNow
            };
            await _notifications.CreateNotificationAsync(notification);
            await _notificationSender.SendToUserAsync(notification);
        }

        private async Task<PlatformAppealDto?> MapAsync(long id)
        {
            var a = await _context.PlatformAppeals.AsNoTracking()
                .Include(x => x.Sanction)
                .Include(x => x.User)
                .Include(x => x.ReviewedByUser)
                .FirstOrDefaultAsync(x => x.Id == id);
            return a == null ? null : Map(a);
        }

        private static PlatformAppealDto Map(PlatformAppeal a) => new()
        {
            Id = a.Id,
            SanctionId = a.SanctionId,
            SanctionType = a.Sanction?.Type ?? string.Empty,
            SanctionStatus = a.Sanction?.Status ?? string.Empty,
            SanctionReason = a.Sanction?.Reason ?? string.Empty,
            SanctionExpiresAt = a.Sanction?.ExpiresAt,
            UserId = a.UserId,
            UserNickname = a.User?.Nickname,
            Message = a.Message,
            Status = a.Status,
            StaffNote = a.StaffNote,
            ReviewedByUserId = a.ReviewedByUserId,
            ReviewedByNickname = a.ReviewedByUser?.Nickname,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            ResolvedAt = a.ResolvedAt
        };
    }
}
