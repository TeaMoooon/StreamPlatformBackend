using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/reports")]
    [Authorize]
    public class ReportsController : ControllerBase
    {
        private readonly IPlatformReportService _reports;

        public ReportsController(IPlatformReportService reports)
        {
            _reports = reports;
        }

        /// <summary>
        /// Create a platform report (any authenticated user).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePlatformReportDto dto)
        {
            var userId = GetCurrentUserId();
            var (report, error) = await _reports.CreateAsync(userId, dto);
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
