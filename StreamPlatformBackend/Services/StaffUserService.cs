using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public interface IStaffUserService
    {
        Task<List<StaffUserSearchDto>> SearchAsync(string query, int take = 20);
        Task<StaffUserDetailDto?> GetByIdAsync(int userId);
        Task<(StaffUserDetailDto? user, string? error)> SetRoleAsync(int actorUserId, string actorRole, int targetUserId, string newRole);
    }

    public class StaffUserService : IStaffUserService
    {
        private readonly AppDbContext _context;
        private readonly IStaffAuditService _audit;
        private readonly IRefreshTokenService _refreshTokens;
        private readonly ILogger<StaffUserService> _logger;

        public StaffUserService(
            AppDbContext context,
            IStaffAuditService audit,
            IRefreshTokenService refreshTokens,
            ILogger<StaffUserService> logger)
        {
            _context = context;
            _audit = audit;
            _refreshTokens = refreshTokens;
            _logger = logger;
        }

        public async Task<List<StaffUserSearchDto>> SearchAsync(string query, int take = 20)
        {
            take = Math.Clamp(take, 1, 50);
            var q = (query ?? string.Empty).Trim();
            if (q.Length < 1)
                return new List<StaffUserSearchDto>();

            var now = DateTime.UtcNow;
            IQueryable<UserModel> users = _context.Users.AsNoTracking();

            if (int.TryParse(q, out var id))
            {
                users = users.Where(u => u.Id == id || u.Nickname.ToLower().Contains(q.ToLower()));
            }
            else
            {
                var lower = q.ToLowerInvariant();
                users = users.Where(u =>
                    u.Nickname.ToLower().Contains(lower) ||
                    u.Email.ToLower().Contains(lower));
            }

            var rows = await users
                .OrderBy(u => u.Nickname)
                .Take(take)
                .Select(u => new
                {
                    u.Id,
                    u.Nickname,
                    u.Email,
                    u.Role,
                    CreatedAt = u.RegistrationDate,
                    ActiveSanctionCount = _context.PlatformSanctions.Count(s =>
                        s.TargetUserId == u.Id &&
                        s.Status == PlatformSanctionStatuses.Active &&
                        (s.ExpiresAt == null || s.ExpiresAt > now))
                })
                .ToListAsync();

            return rows.Select(u => new StaffUserSearchDto
            {
                Id = u.Id,
                Nickname = u.Nickname,
                Email = u.Email,
                Role = UserRole.Normalize(u.Role),
                CreatedAt = u.CreatedAt,
                ActiveSanctionCount = u.ActiveSanctionCount
            }).ToList();
        }

        public async Task<StaffUserDetailDto?> GetByIdAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return null;

            var sanctions = await _context.PlatformSanctions
                .AsNoTracking()
                .Where(s =>
                    s.TargetUserId == userId &&
                    s.Status == PlatformSanctionStatuses.Active &&
                    (s.ExpiresAt == null || s.ExpiresAt > now))
                .OrderByDescending(s => s.CreatedAt)
                .Take(20)
                .Select(s => new StaffUserSanctionBriefDto
                {
                    Id = s.Id,
                    Type = s.Type,
                    Reason = s.Reason,
                    CreatedAt = s.CreatedAt,
                    ExpiresAt = s.ExpiresAt
                })
                .ToListAsync();

            return new StaffUserDetailDto
            {
                Id = user.Id,
                Nickname = user.Nickname,
                Email = user.Email,
                Role = UserRole.Normalize(user.Role),
                CreatedAt = user.RegistrationDate,
                ActiveSanctionCount = sanctions.Count,
                ActiveSanctions = sanctions
            };
        }

        public async Task<(StaffUserDetailDto? user, string? error)> SetRoleAsync(
            int actorUserId,
            string actorRole,
            int targetUserId,
            string newRole)
        {
            // Prefer DB role over JWT claim so a demoted admin cannot keep promoting peers
            // until access-token expiry.
            var actorDbRole = await _context.Users.AsNoTracking()
                .Where(u => u.Id == actorUserId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();
            actorRole = UserRole.Normalize(actorDbRole ?? actorRole);

            if (!UserRole.CanManageStaffRoles(actorRole))
                return (null, "Недостаточно прав для смены роли");

            newRole = UserRole.Normalize(newRole);
            if (!UserRole.IsKnownRole(newRole))
                return (null, "Неизвестная роль");

            if (actorUserId == targetUserId)
                return (null, "Нельзя менять свою роль");

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (target == null)
                return (null, "Пользователь не найден");

            var oldRole = UserRole.Normalize(target.Role);

            if (oldRole == newRole)
                return (await GetByIdAsync(targetUserId), null);

            // Owner-only: grant/revoke Owner or Admin.
            if (actorRole != UserRole.Owner &&
                (newRole is UserRole.Owner or UserRole.Admin || oldRole is UserRole.Owner or UserRole.Admin))
            {
                return (null, "Только Owner может назначать или снимать Owner и Admin");
            }

            if (!UserRole.CanAssignRole(actorRole, newRole))
                return (null, "Недостаточно прав для назначения этой роли");

            // Admin cannot demote/change another Admin (Owner already gated above).
            if (actorRole == UserRole.Admin && oldRole == UserRole.Admin && newRole != UserRole.Admin)
                return (null, "Admin не может снимать роль другого Admin");

            target.Role = newRole;
            await _context.SaveChangesAsync();

            // Fail-closed: access JWTs stay usable until sid revoke. If revoke fails, roll
            // the role back so a demoted admin cannot keep StaffAdmin until token expiry.
            try
            {
                await _refreshTokens.RevokeAllForUserAsync(targetUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to revoke sessions after role change for user {TargetUserId} — reverting role",
                    targetUserId);
                target.Role = oldRole;
                await _context.SaveChangesAsync();
                return (null, "Не удалось сбросить сессии после смены роли. Роль не изменена.");
            }

            await _audit.WriteAsync(
                actorUserId,
                StaffAuditActions.RoleChanged,
                targetUserId: targetUserId,
                entityType: "User",
                entityId: targetUserId.ToString(),
                details: $"{oldRole} → {newRole}");

            _logger.LogInformation(
                "Staff role changed: actor={Actor} target={Target} {Old}→{New}",
                actorUserId,
                targetUserId,
                oldRole,
                newRole);

            return (await GetByIdAsync(targetUserId), null);
        }
    }
}
