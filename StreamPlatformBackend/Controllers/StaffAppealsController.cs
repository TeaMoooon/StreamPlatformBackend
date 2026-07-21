using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/staff/appeals")]
    [Authorize(Policy = "StaffModerator")]
    public class StaffAppealsController : ControllerBase
    {
        private readonly IPlatformAppealService _appeals;

        public StaffAppealsController(IPlatformAppealService appeals)
        {
            _appeals = appeals;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status = "open", [FromQuery] int take = 50)
        {
            var items = await _appeals.GetStaffAsync(status, take);
            return Ok(items);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Review(long id, [FromBody] UpdatePlatformAppealDto dto)
        {
            var staffId = GetCurrentUserId();
            var (appeal, error) = await _appeals.ReviewAsync(staffId, id, dto);
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
