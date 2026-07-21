using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/staff")]
    [Authorize(Policy = "StaffOnly")]
    public class StaffController : ControllerBase
    {
        private readonly IStaffAuditService _staffAuditService;
        private readonly IStaffUserService _staffUsers;
        private readonly IStaffStatsService _staffStats;
        private readonly ILogger<StaffController> _logger;

        public StaffController(
            IStaffAuditService staffAuditService,
            IStaffUserService staffUsers,
            IStaffStatsService staffStats,
            ILogger<StaffController> logger)
        {
            _staffAuditService = staffAuditService;
            _staffUsers = staffUsers;
            _staffStats = staffStats;
            _logger = logger;
        }

        /// <summary>
        /// Who am I in staff context — used to verify roles/JWT wiring.
        /// </summary>
        [HttpGet("me")]
        public Task<IActionResult> Me()
        {
            var userId = GetCurrentUserId();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? UserRole.User;
            var nickname = User.Identity?.Name ?? string.Empty;

            // Do not write audit on every /me poll — floods the log.

            return Task.FromResult<IActionResult>(Ok(new
            {
                userId,
                nickname,
                role,
                permissions = new
                {
                    canAccessStaffPanel = UserRole.CanAccessStaffPanel(role),
                    canManageTickets = UserRole.CanManageTickets(role),
                    canModeratePlatform = UserRole.CanModeratePlatform(role),
                    canManageStaffRoles = UserRole.CanManageStaffRoles(role)
                }
            }));
        }

        /// <summary>
        /// Recent platform staff audit entries (all staff).
        /// </summary>
        [HttpGet("audit")]
        public async Task<IActionResult> GetAudit(
            [FromQuery] int take = 50,
            [FromQuery] bool includeAccess = false)
        {
            var logs = await _staffAuditService.GetRecentAsync(take, includeAccess);
            return Ok(logs.Select(l => new
            {
                l.Id,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.Details,
                l.CreatedAt,
                ActorUserId = l.ActorUserId,
                ActorNickname = l.Actor?.Nickname,
                TargetUserId = l.TargetUserId,
                TargetNickname = l.TargetUser?.Nickname
            }));
        }

        /// <summary>
        /// Basic queue counters for the staff panel header.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _staffStats.GetQueueStatsAsync(TimeSpan.FromHours(24));
            return Ok(stats);
        }

        /// <summary>
        /// Search users by nickname, email, or id.
        /// </summary>
        [HttpGet("users")]
        public async Task<IActionResult> SearchUsers([FromQuery] string q = "", [FromQuery] int take = 20)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return BadRequest(new { message = "Введите минимум 2 символа для поиска" });

            var users = await _staffUsers.SearchAsync(q, take);
            return Ok(users);
        }

        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var user = await _staffUsers.GetByIdAsync(id);
            if (user == null) return NotFound(new { message = "Пользователь не найден" });
            return Ok(user);
        }

        /// <summary>
        /// Assign global role (Admin / SuperAdmin only).
        /// </summary>
        [HttpPut("users/{id:int}/role")]
        [Authorize(Policy = "StaffAdmin")]
        public async Task<IActionResult> SetRole(int id, [FromBody] SetStaffRoleDto dto)
        {
            var actorId = GetCurrentUserId();
            var actorRole = User.FindFirstValue(ClaimTypes.Role) ?? UserRole.User;
            var (user, error) = await _staffUsers.SetRoleAsync(actorId, actorRole, id, dto.Role);
            if (error != null)
                return BadRequest(new { message = error });

            return Ok(user);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
