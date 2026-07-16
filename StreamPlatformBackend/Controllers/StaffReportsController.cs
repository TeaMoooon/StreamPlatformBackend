using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/staff/reports")]
    [Authorize(Policy = "StaffModerator")]
    public class StaffReportsController : ControllerBase
    {
        private readonly IPlatformReportService _reports;

        public StaffReportsController(IPlatformReportService reports)
        {
            _reports = reports;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status = "new", [FromQuery] int take = 50)
        {
            var items = await _reports.ListAsync(status, take);
            return Ok(items);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var report = await _reports.GetByIdAsync(id);
            if (report == null)
                return NotFound(new { message = "Жалоба не найдена" });
            return Ok(report);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdatePlatformReportDto dto)
        {
            var actorId = GetCurrentUserId();
            var (report, error) = await _reports.UpdateAsync(actorId, id, dto);
            if (error != null)
                return BadRequest(new { message = error });

            return Ok(report);
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
