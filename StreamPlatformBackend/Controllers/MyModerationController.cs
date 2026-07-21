using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/me")]
    [Authorize]
    public class MyModerationController : ControllerBase
    {
        private readonly IPlatformSanctionService _sanctions;
        private readonly IPlatformAppealService _appeals;

        public MyModerationController(IPlatformSanctionService sanctions, IPlatformAppealService appeals)
        {
            _sanctions = sanctions;
            _appeals = appeals;
        }

        /// <summary>My platform sanctions (for appeals UI).</summary>
        [HttpGet("sanctions")]
        public async Task<IActionResult> GetMySanctions([FromQuery] bool activeOnly = false)
        {
            var userId = GetCurrentUserId();
            var items = await _sanctions.GetForUserAsync(userId, activeOnly);
            return Ok(items);
        }

        [HttpGet("appeals")]
        public async Task<IActionResult> GetMyAppeals()
        {
            var userId = GetCurrentUserId();
            return Ok(await _appeals.GetMineAsync(userId));
        }

        [HttpPost("appeals")]
        public async Task<IActionResult> CreateAppeal([FromBody] CreatePlatformAppealDto dto)
        {
            var userId = GetCurrentUserId();
            var (appeal, error) = await _appeals.CreateAsync(userId, dto);
            if (error != null)
                return BadRequest(new { message = error });
            return Ok(appeal);
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
