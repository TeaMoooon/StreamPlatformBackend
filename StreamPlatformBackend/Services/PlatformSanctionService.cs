using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Staff;

namespace StreamPlatformBackend.Services
{
    public interface IPlatformSanctionService
    {
        Task<(PlatformSanctionDto? sanction, string? error)> IssueAsync(int actorUserId, IssuePlatformSanctionDto dto);
        Task<(PlatformSanctionDto? sanction, string? error)> RevokeAsync(int actorUserId, long sanctionId, string? reason);
        Task<List<PlatformSanctionDto>> GetForUserAsync(int targetUserId, bool activeOnly = true);
        Task<List<PlatformSanctionDto>> GetActiveAsync(int take = 100);
        Task ExpireOverdueAsync();
        Task<bool> BlocksLoginAsync(int userId);
        Task<bool> BlocksStreamingAsync(int userId);
        Task<bool> BlocksChatAsync(int userId);
        Task<string?> GetBlockMessageAsync(int userId, params string[] types);
    }

    public class PlatformSanctionService : IPlatformSanctionService
    {
        private readonly AppDbContext _context;
        private readonly IStaffAuditService _audit;
        private readonly ILogger<PlatformSanctionService> _logger;

        public PlatformSanctionService(
            AppDbContext context,
            IStaffAuditService audit,
            ILogger<PlatformSanctionService> logger)
        {
            _context = context;
            _audit = audit;
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

        public async Task ExpireOverdueAsync()
        {
            var now = DateTime.UtcNow;
            var overdue = await _context.PlatformSanctions
                .Where(s => s.Status == PlatformSanctionStatuses.Active
                            && s.ExpiresAt != null
                            && s.ExpiresAt <= now)
                .ToListAsync();

            if (overdue.Count == 0) return;

            foreach (var s in overdue)
                s.Status = PlatformSanctionStatuses.Expired;

            await _context.SaveChangesAsync();
        }

        public Task<bool> BlocksLoginAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.LoginBan, PlatformSanctionTypes.FullBan);

        public Task<bool> BlocksStreamingAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.StreamBan, PlatformSanctionTypes.FullBan);

        public Task<bool> BlocksChatAsync(int userId) =>
            HasActiveAsync(userId, PlatformSanctionTypes.ChatMute, PlatformSanctionTypes.FullBan);

        public async Task<string?> GetBlockMessageAsync(int userId, params string[] types)
        {
            await ExpireOverdueAsync();

            var sanction = await _context.PlatformSanctions.AsNoTracking()
                .Where(s => s.TargetUserId == userId
                            && s.Status == PlatformSanctionStatuses.Active
                            && types.Contains(s.Type))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (sanction == null) return null;

            var until = sanction.ExpiresAt.HasValue
                ? $" до {sanction.ExpiresAt.Value:u}"
                : " (бессрочно)";

            return $"Действие заблокировано санкцией платформы ({sanction.Type}){until}: {sanction.Reason}";
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
