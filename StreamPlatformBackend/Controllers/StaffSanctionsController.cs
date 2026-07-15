using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/staff/sanctions")]
    [Authorize(Policy = "StaffModerator")]
    public class StaffSanctionsController : ControllerBase
    {
        private readonly IPlatformSanctionService _sanctions;

        public StaffSanctionsController(IPlatformSanctionService sanctions)
        {
            _sanctions = sanctions;
        }

        [HttpGet]
        public async Task<IActionResult> ListActive([FromQuery] int take = 100)
        {
            var items = await _sanctions.GetActiveAsync(take);
            return Ok(items);
        }

        [HttpGet("user/{userId:int}")]
        public async Task<IActionResult> ListForUser(int userId, [FromQuery] bool activeOnly = true)
        {
            var items = await _sanctions.GetForUserAsync(userId, activeOnly);
            return Ok(items);
        }

        [HttpPost]
        public async Task<IActionResult> Issue([FromBody] IssuePlatformSanctionDto dto)
        {
            var actorId = GetCurrentUserId();
            var (sanction, error) = await _sanctions.IssueAsync(actorId, dto);
            if (error != null)
                return BadRequest(new { message = error });

            return Ok(sanction);
        }

        [HttpPost("{id:long}/revoke")]
        public async Task<IActionResult> Revoke(long id, [FromBody] RevokePlatformSanctionDto? dto)
        {
            var actorId = GetCurrentUserId();
            var (sanction, error) = await _sanctions.RevokeAsync(actorId, id, dto?.Reason);
            if (error != null)
                return BadRequest(new { message = error });

            return Ok(sanction);
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
