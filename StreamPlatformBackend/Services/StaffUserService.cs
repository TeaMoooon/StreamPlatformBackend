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
        private readonly ILogger<StaffUserService> _logger;

        public StaffUserService(
            AppDbContext context,
            IStaffAuditService audit,
            ILogger<StaffUserService> logger)
        {
            _context = context;
            _audit = audit;
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
                Role = u.Role,
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
                Role = user.Role,
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
            if (!UserRole.CanManageStaffRoles(actorRole))
                return (null, "Недостаточно прав для смены роли");

            newRole = (newRole ?? string.Empty).Trim();
            if (!UserRole.IsKnownRole(newRole))
                return (null, "Неизвестная роль");

            if (actorUserId == targetUserId)
                return (null, "Нельзя менять свою роль");

            var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            if (target == null)
                return (null, "Пользователь не найден");

            var oldRole = target.Role;

            if (oldRole == newRole)
                return (await GetByIdAsync(targetUserId), null);

            // Only SuperAdmin may grant/revoke SuperAdmin
            if (actorRole != UserRole.SuperAdmin &&
                (newRole == UserRole.SuperAdmin || oldRole == UserRole.SuperAdmin))
            {
                return (null, "Только SuperAdmin может назначать или снимать SuperAdmin");
            }

            // Admin cannot change another Admin's role (avoid peer demotion wars); SuperAdmin can
            if (actorRole == UserRole.Admin &&
                oldRole == UserRole.Admin &&
                newRole != UserRole.Admin)
            {
                return (null, "Admin не может снимать роль другого Admin");
            }

            target.Role = newRole;
            await _context.SaveChangesAsync();

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
