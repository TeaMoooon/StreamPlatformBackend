using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public interface IPlatformSanctionService
    {
        Task<(PlatformSanctionDto? sanction, string? error)> IssueAsync(int actorUserId, IssuePlatformSanctionDto dto);
        Task<(PlatformSanctionDto? sanction, string? error)> RevokeAsync(int actorUserId, long sanctionId, string? reason);
        Task<List<PlatformSanctionDto>> GetForUserAsync(int targetUserId, bool activeOnly = true);
        Task<List<PlatformSanctionDto>> GetActiveAsync(int take = 100);
        /// <summary>Marks overdue temporary sanctions as expired. Returns how many were expired.</summary>
        Task<int> ExpireOverdueAsync();
        Task<bool> BlocksLoginAsync(int userId);
        Task<bool> BlocksStreamingAsync(int userId);
        Task<bool> BlocksChatAsync(int userId);
        Task<string?> GetBlockMessageAsync(int userId, params string[] types);
        Task<ActiveBlockInfo?> GetActiveBlockAsync(int userId, params string[] types);
    }

    public sealed class ActiveBlockInfo
    {
        public long SanctionId { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public DateTime? ExpiresAt { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public class PlatformSanctionService : IPlatformSanctionService
    {
        private readonly AppDbContext _context;
        private readonly IStaffAuditService _audit;
        private readonly INotificationRepository _notifications;
        private readonly INotificationSender _notificationSender;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PlatformSanctionService> _logger;

        public PlatformSanctionService(
            AppDbContext context,
            IStaffAuditService audit,
            INotificationRepository notifications,
            INotificationSender notificationSender,
            IServiceScopeFactory scopeFactory,
            ILogger<PlatformSanctionService> logger)
        {
            _context = context;
            _audit = audit;
            _notifications = notifications;
            _notificationSender = notificationSender;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<(PlatformSanctionDto? sanction, string? error)> IssueAsync(int actorUserId, IssuePlatformSanctionDto dto)
        {
            if (dto.TargetUserId <= 0)
                return (null, "Не указан пользователь");

            var type = (dto.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (!PlatformSanctionTypes.IsKnown(type))
                return (null, "Неизвестный тип санкции");

            var reason = (dto.Reason ?? string.Empty).Trim();
            if (reason.Length < 3)
                return (null, "Укажите причину (минимум 3 символа)");
            if (reason.Length > 500)
                return (null, "Причина слишком длинная");

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.TargetUserId);
            if (target == null)
                return (null, "Пользователь не найден");

            if (dto.TargetUserId == actorUserId)
                return (null, "Нельзя применить санкцию к себе");

            if (UserRole.IsStaff(target.Role) && !UserRole.CanManageStaffRoles(
                    (await _context.Users.Where(u => u.Id == actorUserId).Select(u => u.Role).FirstOrDefaultAsync())))
            {
                return (null, "Недостаточно прав для санкции в отношении staff");
            }

            await ExpireOverdueAsync();

            DateTime? expiresAt = null;
            if (dto.DurationMinutes is > 0)
                expiresAt = DateTime.UtcNow.AddMinutes(dto.DurationMinutes.Value);

            var sanction = new PlatformSanction
            {
                TargetUserId = dto.TargetUserId,
                Type = type,
                Status = PlatformSanctionStatuses.Active,
                Reason = reason,
                IssuedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };

            _context.PlatformSanctions.Add(sanction);
            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                actorUserId,
                StaffAuditActions.SanctionIssued,
                targetUserId: dto.TargetUserId,
                entityType: "PlatformSanction",
                entityId: sanction.Id.ToString(),
                details: $"{type}: {reason}");

            await NotifyUserAsync(
                dto.TargetUserId,
                sanction.Id,
                type,
                BuildIssueMessage(type, reason, expiresAt));

            if (type is PlatformSanctionTypes.StreamBan or PlatformSanctionTypes.FullBan)
            {
                try
                {
                    // Resolve via scope to avoid circular DI with StreamService -> IPlatformSanctionService.
                    using var scope = _scopeFactory.CreateScope();
                    var streams = scope.ServiceProvider.GetRequiredService<IStreamService>();
                    await streams.ForceTerminateActiveStreamAsync(
                        dto.TargetUserId,
                        reason: $"sanction:{type}:{sanction.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to force-end stream after {Type} for user {TargetUserId}",
                        type, dto.TargetUserId);
                }
            }

            _logger.LogWarning(
                "Platform sanction {SanctionId} ({Type}) issued to user {TargetUserId} by {ActorUserId}",
                sanction.Id, type, dto.TargetUserId, actorUserId);

            return (await MapAsync(sanction.Id), null);
        }

        public async Task<(PlatformSanctionDto? sanction, string? error)> RevokeAsync(int actorUserId, long sanctionId, string? reason)
        {
            await ExpireOverdueAsync();

            var sanction = await _context.PlatformSanctions.FirstOrDefaultAsync(s => s.Id == sanctionId);
            if (sanction == null)
                return (null, "Санкция не найдена");

            if (sanction.Status != PlatformSanctionStatuses.Active)
                return (null, "Санкция уже не активна");

            sanction.Status = PlatformSanctionStatuses.Revoked;
            sanction.RevokedAt = DateTime.UtcNow;
            sanction.RevokedByUserId = actorUserId;
            sanction.RevokeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                actorUserId,
                StaffAuditActions.SanctionRevoked,
                targetUserId: sanction.TargetUserId,
                entityType: "PlatformSanction",
                entityId: sanction.Id.ToString(),
                details: sanction.RevokeReason);

            await NotifyUserAsync(
                sanction.TargetUserId,
                sanction.Id,
                sanction.Type,
                string.IsNullOrWhiteSpace(sanction.RevokeReason)
                    ? $"С вас сняли наказание ({FriendlyType(sanction.Type)})."
                    : $"С вас сняли наказание ({FriendlyType(sanction.Type)}): {sanction.RevokeReason}");

            return (await MapAsync(sanction.Id), null);
        }

        public async Task<List<PlatformSanctionDto>> GetForUserAsync(int targetUserId, bool activeOnly = true)
        {
            await ExpireOverdueAsync();

            var query = _context.PlatformSanctions.AsNoTracking()
                .Where(s => s.TargetUserId == targetUserId);

            if (activeOnly)
                query = query.Where(s => s.Status == PlatformSanctionStatuses.Active);

            var items = await query
                .OrderByDescending(s => s.CreatedAt)
                .Take(100)
                .Include(s => s.TargetUser)
                .Include(s => s.IssuedByUser)
                .Include(s => s.RevokedByUser)
                .ToListAsync();

            return items.Select(Map).ToList();
        }

        public async Task<List<PlatformSanctionDto>> GetActiveAsync(int take = 100)
        {
            await ExpireOverdueAsync();
            take = Math.Clamp(take, 1, 200);

            var items = await _context.PlatformSanctions.AsNoTracking()
                .Where(s => s.Status == PlatformSanctionStatuses.Active)
                .OrderByDescending(s => s.CreatedAt)
                .Take(take)
                .Include(s => s.TargetUser)
                .Include(s => s.IssuedByUser)
                .Include(s => s.RevokedByUser)
                .ToListAsync();

            return items.Select(Map).ToList();
        }

        public async Task<int> ExpireOverdueAsync()
        {
            var now = DateTime.UtcNow;
            var overdue = await _context.PlatformSanctions
                .Where(s => s.Status == PlatformSanctionStatuses.Active
                            && s.ExpiresAt != null
                            && s.ExpiresAt <= now)
                .ToListAsync();

            if (overdue.Count == 0) return 0;

            foreach (var s in overdue)
                s.Status = PlatformSanctionStatuses.Expired;

            await _context.SaveChangesAsync();

            foreach (var s in overdue)
            {
                try
                {
                    await NotifyUserAsync(
                        s.TargetUserId,
                        s.Id,
                        s.Type,
                        $"Срок наказания ({FriendlyType(s.Type)}) истёк — ограничение снято");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify user {UserId} about expired sanction {Id}", s.TargetUserId, s.Id);
                }
            }

            return overdue.Count;
        }

        private static string FriendlyType(string type) => type switch
        {
            PlatformSanctionTypes.Warning => "предупреждение",
            PlatformSanctionTypes.ChatMute => "мут чата",
            PlatformSanctionTypes.StreamBan => "бан стрима",
            PlatformSanctionTypes.LoginBan => "бан входа",
            PlatformSanctionTypes.FullBan => "полный бан",
            _ => type
        };

        private static string BuildIssueMessage(string type, string reason, DateTime? expiresAt)
        {
            var until = expiresAt.HasValue
                ? $" Срок: до {expiresAt.Value:dd.MM.yyyy HH:mm} UTC."
                : " Срок: бессрочно.";
            return $"Вам выдали наказание: {FriendlyType(type)}. Причина: {reason}.{until} Обжаловать можно в Настройки → Поддержка.";
        }

        public Task<bool> BlocksLoginAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.LoginBan, PlatformSanctionTypes.FullBan);

        public Task<bool> BlocksStreamingAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.StreamBan, PlatformSanctionTypes.FullBan);

        public Task<bool> BlocksChatAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.ChatMute, PlatformSanctionTypes.FullBan);

        public async Task<string?> GetBlockMessageAsync(int userId, params string[] types)
        {
            var info = await GetActiveBlockAsync(userId, types);
            return info?.Message;
        }

        public async Task<ActiveBlockInfo?> GetActiveBlockAsync(int userId, params string[] types)
        {
            await ExpireOverdueAsync();

            var sanction = await _context.PlatformSanctions.AsNoTracking()
                .Where(s => s.TargetUserId == userId
                            && s.Status == PlatformSanctionStatuses.Active
                            && types.Contains(s.Type))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (sanction == null) return null;

            var typeLabel = FriendlyType(sanction.Type);
            var until = sanction.ExpiresAt.HasValue
                ? $"Срок: до {sanction.ExpiresAt.Value:dd.MM.yyyy HH:mm} UTC"
                : "Срок: бессрочно";

            var headline = sanction.Type switch
            {
                PlatformSanctionTypes.LoginBan => "Вход запрещён",
                PlatformSanctionTypes.ChatMute => "Чат недоступен",
                PlatformSanctionTypes.StreamBan => "Трансляция запрещена",
                PlatformSanctionTypes.FullBan => "Аккаунт заблокирован",
                _ => $"Ограничение: {typeLabel}"
            };

            return new ActiveBlockInfo
            {
                SanctionId = sanction.Id,
                Type = sanction.Type,
                Reason = sanction.Reason,
                ExpiresAt = sanction.ExpiresAt,
                Message = $"{headline} ({typeLabel}). {until}. Причина: {sanction.Reason}"
            };
        }

        private async Task NotifyUserAsync(int userId, long sanctionId, string type, string message)
        {
            var typeLabel = type switch
            {
                PlatformSanctionTypes.Warning => "Предупреждение",
                PlatformSanctionTypes.ChatMute => "Мут чата",
                PlatformSanctionTypes.StreamBan => "Бан стрима",
                PlatformSanctionTypes.LoginBan => "Бан входа",
                PlatformSanctionTypes.FullBan => "Полный бан",
                _ => "Наказание"
            };

            var notification = new NotificationModel
            {
                UserId = userId,
                Type = NotificationType.PlatformSanction,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    SanctionId = sanctionId,
                    Type = type,
                    Title = typeLabel,
                    Message = message
                }),
                CreatedAt = DateTime.UtcNow
            };
            await _notifications.CreateNotificationAsync(notification);
            try
            {
                await _notificationSender.SendToUserAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push sanction notification to user {UserId}", userId);
            }
        }

        private async Task<bool> HasActiveAsync(int userId, params string[] types)
        {
            if (userId <= 0) return false;
            await ExpireOverdueAsync();

            return await _context.PlatformSanctions.AsNoTracking()
                .AnyAsync(s => s.TargetUserId == userId
                               && s.Status == PlatformSanctionStatuses.Active
                               && types.Contains(s.Type));
        }

        private async Task<PlatformSanctionDto?> MapAsync(long id)
        {
            var s = await _context.PlatformSanctions.AsNoTracking()
                .Include(x => x.TargetUser)
                .Include(x => x.IssuedByUser)
                .Include(x => x.RevokedByUser)
                .FirstOrDefaultAsync(x => x.Id == id);
            return s == null ? null : Map(s);
        }

        private static PlatformSanctionDto Map(PlatformSanction s) => new()
        {
            Id = s.Id,
            TargetUserId = s.TargetUserId,
            TargetNickname = s.TargetUser?.Nickname,
            Type = s.Type,
            Status = s.Status,
            Reason = s.Reason,
            IssuedByUserId = s.IssuedByUserId,
            IssuedByNickname = s.IssuedByUser?.Nickname,
            RevokedByUserId = s.RevokedByUserId,
            RevokedByNickname = s.RevokedByUser?.Nickname,
            RevokeReason = s.RevokeReason,
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            RevokedAt = s.RevokedAt
        };
    }
}
