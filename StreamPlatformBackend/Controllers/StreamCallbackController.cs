using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Services;
using System.Diagnostics;
using System.IO;

[ApiController]
[Route("api/[controller]")]
public class StreamCallbackController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly ILogger<StreamCallbackController> _logger;
    private readonly AppDbContext _context;
    private const string RTMP_SECRET = "your-secret-value";

    public StreamCallbackController(IStreamService streamService, ILogger<StreamCallbackController> logger, AppDbContext context)
    {
        _streamService = streamService;
        _logger = logger;
        _context = context;
    }

    [HttpPost("start")]
    public async Task<IActionResult> OnStreamStart([FromQuery] string secret)
    {
        try
        {
            var form = await Request.ReadFormAsync();
            string streamKey = form["name"];

            _logger.LogInformation("=== STREAM START CALLBACK === streamKey={Key}", streamKey);

            if (string.IsNullOrEmpty(streamKey))
                return BadRequest("Stream key is required");

            if (secret != RTMP_SECRET)
                return Unauthorized("Invalid secret");

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return Unauthorized("Invalid stream key format");

            await _streamService.StartStreamAsync(userId, streamKey);
            return Ok();
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
            var form = await Request.ReadFormAsync();
            string streamKey = form["name"];

            _logger.LogInformation("=== STREAM END CALLBACK === streamKey={Key}", streamKey);
            if (string.IsNullOrEmpty(streamKey)) return Ok();

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return Ok();
            if (secret != RTMP_SECRET) return Ok();

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
}
