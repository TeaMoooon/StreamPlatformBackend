using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Services;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class StreamCallbackController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly ILogger<StreamCallbackController> _logger;
    private readonly AppDbContext _context;
    private readonly string _rtmpSecret;

    public StreamCallbackController(
        IStreamService streamService,
        ILogger<StreamCallbackController> logger,
        AppDbContext context,
        IConfiguration configuration)
    {
        _streamService = streamService;
        _logger = logger;
        _context = context;
        _rtmpSecret = configuration["Rtmp:Secret"] ?? "your-secret-value";
    }

    [HttpPost("start")]
    public async Task<IActionResult> OnStreamStart([FromQuery] string secret)
    {
        try
        {
            var streamKey = await ReadStreamKeyFromCallbackAsync();
            if (string.IsNullOrEmpty(streamKey))
                return BadRequest("Stream key is required");

            _logger.LogInformation("=== STREAM START CALLBACK === streamKey={Key}", streamKey);

            if (secret != _rtmpSecret)
                return Unauthorized("Invalid secret");

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return Unauthorized("Invalid stream key format");

            await _streamService.StartStreamAsync(userId, streamKey);
            return Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            // nginx-rtmp / OBS не показывают этот текст пользователю — только отклоняют publish.
            // Текст остаётся в логах и теле ответа для отладки.
            _logger.LogWarning("Stream start denied: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Stream start bad request");
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== STREAM START ERROR ===");
            return StatusCode(500);
        }
    }

    [HttpPost("end")]
    public async Task<IActionResult> OnStreamEnd([FromQuery] string secret)
    {
        try
        {
            var streamKey = await ReadStreamKeyFromCallbackAsync();

            _logger.LogInformation("=== STREAM END CALLBACK === streamKey={Key}", streamKey);
            if (string.IsNullOrEmpty(streamKey)) return Ok();

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return Ok();
            if (secret != _rtmpSecret) return Ok();

            await _streamService.EndStreamAsync(userId, streamKey);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== STREAM END ERROR ===");
            return Ok();
        }
    }

    [HttpPost("ping")]
    public async Task<IActionResult> OnPing([FromQuery(Name = "stream_key")] string streamKey)
    {
        try
        {
            if (string.IsNullOrEmpty(streamKey)) return BadRequest();
            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return BadRequest();

            await _streamService.UpdateHeartbeatAsync(userId, streamKey);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping error for {Key}", streamKey);
            return Ok();
        }
    }

    private bool TryParseUserIdFromStreamKey(string streamKey, out int userId)
    {
        userId = 0;
        if (string.IsNullOrEmpty(streamKey) || !streamKey.StartsWith("live_")) return false;

        var parts = streamKey.Split('_', 3); // split максимум на 3 части, Guid после userId
        return parts.Length >= 2 && int.TryParse(parts[1], out userId);
    }

    /// <summary>
    /// nginx-rtmp: form field <c>name</c>. SRS: JSON field <c>stream</c>.
    /// </summary>
    private async Task<string?> ReadStreamKeyFromCallbackAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var name = form["name"].ToString();
            if (!string.IsNullOrEmpty(name))
                return NormalizeStreamKey(name);
        }

        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.TryGetProperty("stream", out var streamEl))
                return NormalizeStreamKey(streamEl.GetString());
        }

        return null;
    }

    private static string? NormalizeStreamKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // SRS иногда отдаёт stream с суффиксом (.live и т.п.)
        var key = raw.Split('?')[0];
        var dot = key.IndexOf('.');
        if (dot > 0 && key.StartsWith("live_", StringComparison.Ordinal))
            key = key[..dot];

        return key;
    }
}
