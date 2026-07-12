using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/streamers")]
    public class StreamDashboardController : ControllerBase
    {
        private readonly IStreamDashboardService _streamDashboardService;
        private readonly IStreamTeamService _streamTeamService;
        private readonly ILogger<StreamDashboardController> _logger;

        public StreamDashboardController(
            IStreamDashboardService streamDashboardService,
            IStreamTeamService streamTeamService,
            ILogger<StreamDashboardController> logger)
        {
            _streamDashboardService = streamDashboardService;
            _streamTeamService = streamTeamService;
            _logger = logger;
        }

        [HttpGet("{streamerId:int}/stream/settings")]
        public async Task<IActionResult> GetSettings(int streamerId)
        {
            try
            {
                if (!await CanManageStreamAsync(streamerId))
                    return Forbid();

                var settings = await _streamDashboardService.GetSettingsAsync(streamerId);
                if (settings == null)
                    return NotFound(new { message = "Канал не найден" });

                return Ok(settings);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stream settings for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить настройки эфира" });
            }
        }

        [HttpPut("{streamerId:int}/stream/settings")]
        public async Task<IActionResult> UpdateSettings(int streamerId, [FromBody] UpdateStreamDashboardSettingsDto dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var (success, error) = await _streamDashboardService.UpdateSettingsAsync(streamerId, userId, dto);

                if (!success)
                    return BadRequest(new { message = error });

                var settings = await _streamDashboardService.GetSettingsAsync(streamerId);
                return Ok(settings);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating stream settings for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось обновить настройки эфира" });
            }
        }

        [HttpPut("{streamerId:int}/stream/preview")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPreview(int streamerId, IFormFile previewImage)
        {
            try
            {
                var userId = GetCurrentUserId();
                var (success, error, previewUrl) = await _streamDashboardService.UploadPreviewAsync(
                    streamerId,
                    userId,
                    previewImage);

                if (!success)
                    return BadRequest(new { message = error });

                var settings = await _streamDashboardService.GetSettingsAsync(streamerId);
                return Ok(new { previewUrl, settings });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading stream preview for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить превью" });
            }
        }

        private async Task<bool> CanManageStreamAsync(int streamerId)
        {
            var userId = GetCurrentUserId();
            var access = await _streamTeamService.GetAccessAsync(streamerId, userId);
            return access != null && access.CanManageStream;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
