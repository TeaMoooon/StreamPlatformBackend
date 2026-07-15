using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Constants;
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
        private readonly ILogger<StaffController> _logger;

        public StaffController(IStaffAuditService staffAuditService, ILogger<StaffController> logger)
        {
            _staffAuditService = staffAuditService;
            _logger = logger;
        }

        /// <summary>
        /// Who am I in staff context — used to verify roles/JWT wiring.
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId = GetCurrentUserId();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? UserRole.User;
            var nickname = User.Identity?.Name ?? string.Empty;

            await _staffAuditService.WriteAsync(
                userId,
                StaffAuditActions.StaffAccess,
                details: $"Accessed /api/staff/me as {role}");

            return Ok(new
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
            });
        }

        /// <summary>
        /// Recent platform staff audit entries (Admin+).
        /// </summary>
        [HttpGet("audit")]
        [Authorize(Policy = "StaffAdmin")]
        public async Task<IActionResult> GetAudit([FromQuery] int take = 50)
        {
            var logs = await _staffAuditService.GetRecentAsync(take);
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

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
