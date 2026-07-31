using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
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
    /// Получить видео/стрим для просмотра.
    /// Если стрим активный — возвращает HLS.
    /// Если завершён — возвращает ссылку на MP4.
    /// </summary>
    /// <param name="streamId">ID стрима</param>
    /// <returns>Тип и URL для просмотра</returns>
    [HttpGet("streams/{streamId}/watch")]
    public async Task<IActionResult> WatchStream(int streamId)
    {
        var stream = await _context.Streams
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == streamId);

        if (stream == null)
            return NotFound(new { message = "Стрим не найден" });

        // --- Активный стрим ---
        if (stream.EndedAt == null)
        {
            var playbackId = string.IsNullOrWhiteSpace(stream.PublicId)
                ? stream.Id.ToString()
                : stream.PublicId;
            var hlsUrl = $"/hls/{playbackId}/master.m3u8";
            return Ok(new { type = "live", url = hlsUrl });
        }

        // --- Стрим завершён ---
        if (stream.RecordEnabled && !string.IsNullOrEmpty(stream.RecordPath))
        {
            // Проверяем, что файл существует
            if (!System.IO.File.Exists(stream.RecordPath))
                return BadRequest(new { message = "Запись отсутствует" });

            // Превращаем абсолютный путь в URL
            // /var/www/streamplatform/media/... → /media/...
            var url = stream.RecordPath.Replace("/var/www/streamplatform", "");

            return Ok(new { type = "record", url });
        }

        return BadRequest(new { message = "Стрим недоступен" });
    }


}
