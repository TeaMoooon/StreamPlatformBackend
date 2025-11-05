using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StreamController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly IUserService _userService;
    private readonly ILogger<StreamController> _logger;

    public StreamController(IStreamService streamService, IUserService userService, ILogger<StreamController> logger)
    {
        _streamService = streamService;
        _userService = userService;
        _logger = logger;
    }

    // PUT api/stream - обновить информацию о стриме (название, категорию и т.д.)
    [HttpPut]
    public async Task<IActionResult> UpdateStream([FromBody] StreamUpdateDto updateDto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _streamService.UpdateStreamAsync(userId, updateDto);

            if (!result)
                return BadRequest("No active stream found or update failed");

            return Ok(new { message = "Stream updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating stream for user {UserId}", GetCurrentUserId());
            return StatusCode(500, "Internal server error");
        }
    }

    // GET api/stream/status - статус текущего стрима
    [HttpGet("status")]
    public async Task<IActionResult> GetStreamStatus()
    {
        try
        {
            var userId = GetCurrentUserId();
            var isStreaming = await _streamService.IsUserStreamingAsync(userId);
            var streamInfo = await _streamService.GetStreamInfoAsync(userId);

            return Ok(new
            {
                isStreaming,
                streamInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stream status for user {UserId}", GetCurrentUserId());
            return StatusCode(500, "Internal server error");
        }
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}