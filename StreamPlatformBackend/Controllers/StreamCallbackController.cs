using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Services;

[ApiController]
[Route("api/[controller]")]
public class StreamCallbackController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly ILogger<StreamCallbackController> _logger;

    public StreamCallbackController(IStreamService streamService, ILogger<StreamCallbackController> logger)
    {
        _streamService = streamService;
        _logger = logger;
    }

    // Вызывается nginx когда OBS начинает трансляцию
    [HttpPost("start")]
    public async Task<IActionResult> OnStreamStart([FromForm] string name) // name = streamKey
    {
        try
        {
            _logger.LogInformation("Stream start callback received for stream key: {StreamKey}", name);

            if (string.IsNullOrEmpty(name))
                return BadRequest("Stream key is required");

            // Валидируем stream key
            if (!await _streamService.ValidateStreamKeyAsync(name))
            {
                _logger.LogWarning("Invalid stream key: {StreamKey}", name);
                return Unauthorized("Invalid stream key");
            }

            // Парсим userId из stream key
            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Failed to parse user ID from stream key: {StreamKey}", name);
                return Unauthorized("Invalid stream key format");
            }

            // Запускаем стрим
            await _streamService.StartStreamAsync(userId, name);

            _logger.LogInformation("Stream started successfully for user {UserId}", userId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream start for key: {StreamKey}", name);
            return StatusCode(500, "Internal server error");
        }
    }

    // Вызывается nginx когда OBS останавливает трансляцию
    [HttpPost("end")]
    public async Task<IActionResult> OnStreamEnd([FromForm] string name) // name = streamKey
    {
        try
        {
            _logger.LogInformation("Stream end callback received for stream key: {StreamKey}", name);

            if (string.IsNullOrEmpty(name))
                return BadRequest("Stream key is required");

            // Парсим userId из stream key
            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Failed to parse user ID from stream key: {StreamKey}", name);
                return Ok(); // Всегда возвращаем 200 для nginx
            }

            // Останавливаем стрим
            await _streamService.EndStreamAsync(userId, name);

            _logger.LogInformation("Stream ended successfully for user {UserId}", userId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream end for key: {StreamKey}", name);
            return Ok(); // Всегда возвращаем 200 для nginx
        }
    }

    private bool TryParseUserIdFromStreamKey(string streamKey, out int userId)
    {
        userId = 0;
        if (string.IsNullOrEmpty(streamKey) || !streamKey.StartsWith("live_"))
            return false;

        var parts = streamKey.Split('_');
        return parts.Length >= 2 && int.TryParse(parts[1], out userId);
    }
}