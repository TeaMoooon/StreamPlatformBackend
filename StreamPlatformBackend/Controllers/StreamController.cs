using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
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
    private readonly AppDbContext _context;

    public StreamController(AppDbContext context, IStreamService streamService, IUserService userService, ILogger<StreamController> logger)
    {
        _streamService = streamService;
        _userService = userService;
        _logger = logger;
        _context = context;
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


    /// <summary>
    /// Получить видео/стрим для просмотра
    /// </summary>
    /// <param name="streamId">ID стрима</param>
    /// <returns>Ссылка на HLS поток или MP4 запись</returns>
    [HttpGet("streams/{streamId}/watch")]
    public async Task<IActionResult> WatchStream(int streamId)
    {
        var stream = await _context.Streams.Include(s => s.User)
                                           .FirstOrDefaultAsync(s => s.Id == streamId);
        if (stream == null)
            return NotFound();

        if (stream.EndedAt == null)
        {
            // Стрим в эфире — возвращаем HLS ссылку
            var hlsUrl = $"/hls/{stream.User.StreamKey}.m3u8";
            return Ok(new { Type = "live", Url = hlsUrl });
        }

        if (stream.RecordEnabled && !string.IsNullOrEmpty(stream.RecordPath))
        {
            // Завершённый стрим — возвращаем относительный путь к записи
            var recordUrl = $"/media/users/{stream.UserId}/streams/{stream.Id}/record.mp4";
            return Ok(new { Type = "record", Url = recordUrl });
        }

        return BadRequest(new { message = "Стрим недоступен" });
    }

}
