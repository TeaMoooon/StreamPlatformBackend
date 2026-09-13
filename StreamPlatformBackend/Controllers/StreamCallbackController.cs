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
    private readonly bool _isRtmpSecretConfigured;

    public StreamCallbackController(
        IStreamService streamService,
        ILogger<StreamCallbackController> logger,
        AppDbContext context,
        IConfiguration configuration)
    {
        _streamService = streamService;
        _logger = logger;
        _context = context;
        _rtmpSecret = configuration["Rtmp:Secret"]?.Trim() ?? string.Empty;
        _isRtmpSecretConfigured = RtmpCallbackAuth.IsConfigured(_rtmpSecret);
    }

    [HttpPost("start")]
    public async Task<IActionResult> OnStreamStart()
    {
        try
        {
            var authFailure = ValidateRtmpSecret();
            if (authFailure != null)
                return authFailure;

            var streamKey = await ReadStreamKeyFromCallbackAsync();
            if (string.IsNullOrEmpty(streamKey))
                return BadRequest("Stream key is required");

            _logger.LogInformation("=== STREAM START CALLBACK === streamKey={Key}", streamKey);

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return BadRequest("Invalid stream key format");

            var userStreamKey = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.StreamKey)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(userStreamKey) || !string.Equals(userStreamKey, streamKey, StringComparison.Ordinal))
                return Unauthorized("Invalid stream key");

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
    public async Task<IActionResult> OnStreamEnd()
    {
        try
        {
            var authFailure = ValidateRtmpSecret();
            if (authFailure != null)
                return authFailure;

            var streamKey = await ReadStreamKeyFromCallbackAsync();

            _logger.LogInformation("=== STREAM END CALLBACK === streamKey={Key}", streamKey);
            if (string.IsNullOrEmpty(streamKey))
                return BadRequest(new { message = "Stream key is required" });

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return BadRequest(new { message = "Invalid stream key format" });

            await _streamService.EndStreamAsync(userId, streamKey);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== STREAM END ERROR ===");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Internal error" });
        }
    }

    [HttpPost("ping")]
    public async Task<IActionResult> OnPing([FromQuery(Name = "stream_key")] string streamKey)
    {
        try
        {
            if (string.IsNullOrEmpty(streamKey)) return BadRequest();

            var authFailure = ValidateRtmpSecret();
            if (authFailure != null)
                return authFailure;

            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return BadRequest();
            var userStreamKey = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.StreamKey)
                .FirstOrDefaultAsync();
            if (string.IsNullOrEmpty(userStreamKey) || !string.Equals(userStreamKey, streamKey, StringComparison.Ordinal))
                return Unauthorized();

            await _streamService.UpdateHeartbeatAsync(userId, streamKey);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping error for {Key}", streamKey);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Internal error" });
        }
    }

    private IActionResult? ValidateRtmpSecret()
    {
        if (!_isRtmpSecretConfigured)
            return Unauthorized("RTMP secret is not configured");

        var provided = RtmpCallbackAuth.ReadProvidedSecret(Request);

        // Migration fallback: old nginx-rtmp configs still pass ?secret= on loopback.
        // Prefer X-Rtmp-Secret via 127.0.0.1:5155 proxy; query auth is loopback-only
        // so the value is not accepted from the public network.
        if (provided == null &&
            Request.Query.TryGetValue("secret", out var querySecret) &&
            RtmpCallbackAuth.IsLoopback(Request) &&
            !string.IsNullOrEmpty(querySecret.ToString()))
        {
            _logger.LogWarning(
                "RTMP callback used query-string secret from loopback; migrate on_publish to http://127.0.0.1:5155/rtmp/on_publish");
            provided = querySecret.ToString();
        }

        if (!RtmpCallbackAuth.SecretsMatch(_rtmpSecret, provided))
            return Unauthorized("Invalid secret");

        return null;
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
            try
            {
                var form = await Request.ReadFormAsync();
                var name = form["name"].ToString();
                if (!string.IsNullOrEmpty(name))
                    return NormalizeStreamKey(name);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return null;
            }
        }

        if (Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.TryGetProperty("stream", out var streamEl))
                {
                    if (streamEl.ValueKind != JsonValueKind.String)
                        return null;

                    return NormalizeStreamKey(streamEl.GetString());
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // Адверсиальные/битые payload'ы должны давать корректный 400,
                // а не приводить к 500 из-за необработанного исключения.
                return null;
            }
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
