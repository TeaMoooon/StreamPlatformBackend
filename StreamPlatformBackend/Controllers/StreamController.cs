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

    /// <summary>Обновляет текущий стрим пользователя</summary>
    [HttpPut]
    public async Task<IActionResult> UpdateStream([FromBody] StreamUpdateDto updateDto)
    {
        var userId = GetCurrentUserId();
        var success = await _streamService.UpdateStreamAsync(userId, updateDto);
        if (!success) return BadRequest("No active stream found or update failed");
        return Ok(new { message = "Stream updated successfully" });
    }

    /// <summary>Возвращает статус текущего стрима пользователя</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStreamStatus()
    {
        var userId = GetCurrentUserId();
        var isStreaming = await _streamService.IsUserStreamingAsync(userId);
        var streamInfo = await _streamService.GetStreamInfoAsync(userId);
        return Ok(new { isStreaming, streamInfo });
    }

    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    }
}
