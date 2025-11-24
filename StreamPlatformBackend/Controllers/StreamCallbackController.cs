using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Services;
using System.Diagnostics;

[ApiController]
[Route("api/[controller]")]
public class StreamCallbackController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly ILogger<StreamCallbackController> _logger;
    private readonly AppDbContext _context;

    public StreamCallbackController(IStreamService streamService, ILogger<StreamCallbackController> logger, AppDbContext context)
    {
        _streamService = streamService;
        _logger = logger;
        _context = context;
    }

    // Вызывается nginx когда OBS начинает трансляцию
    /*[HttpPost("start")]
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

    */

    [HttpPost("start")]
    public async Task<IActionResult> OnStreamStart([FromForm] string name)
    {
        try
        {
            _logger.LogInformation("=== STREAM START CALLBACK ===");
            _logger.LogInformation("Received stream key: {StreamKey}", name);
            _logger.LogInformation("Request headers: {@Headers}", Request.Headers);

            if (string.IsNullOrEmpty(name))
            {
                _logger.LogWarning("Stream key is empty");
                return BadRequest("Stream key is required");
            }

            _logger.LogInformation("Parsing user ID from stream key...");

            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Failed to parse user ID from: {StreamKey}", name);
                return Unauthorized("Invalid stream key format");
            }

            _logger.LogInformation("Parsed user ID: {UserId}", userId);
            _logger.LogInformation("Validating stream key...");

            // ВРЕМЕННО: закомментируй проверку для тестов
            // if (!await _streamService.ValidateStreamKeyAsync(name))
            // {
            //     _logger.LogWarning("Stream key validation failed: {StreamKey}", name);
            //     return Unauthorized("Invalid stream key");
            // }

            _logger.LogInformation("Starting stream for user {UserId}...", userId);

            await _streamService.StartStreamAsync(userId, name);

            _logger.LogInformation("=== STREAM START SUCCESS ===");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== STREAM START ERROR ===");
            return StatusCode(500, "Internal server error");
        }
    }

    // Вызывается nginx когда OBS останавливает трансляцию
    // -------------------------------------------------------------
    // STREAM END + FILE PROCESSING
    // -------------------------------------------------------------
    [HttpPost("end")]
    public async Task<IActionResult> OnStreamEnd([FromForm] string name)
    {
        try
        {
            _logger.LogInformation("=== STREAM END CALLBACK === Raw key: {Key}", name);

            if (string.IsNullOrEmpty(name))
                return Ok();

            // Парсим userId из stream key
            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Invalid stream key format on END: {Key}", name);
                return Ok();
            }

            // Закрываем стрим в БД
            await _streamService.EndStreamAsync(userId, name);

            // Ищем завершённый стрим
            var stream = await _context.Streams
                .FirstOrDefaultAsync(s =>
                    s.UserId == userId &&
                    s.EndedAt != null &&
                    s.RecordEnabled
                );

            if (stream == null)
            {
                _logger.LogWarning("No ended stream found for user {UserId}", userId);
                return Ok();
            }

            // -------------------------------------------------------------
            // 1. Находим файл, записанный nginx
            // -------------------------------------------------------------
            var sourceDir = "/var/www/streamplatform/records/";
            var sourceFile = Path.Combine(sourceDir, $"{name}.flv");

            if (!System.IO.File.Exists(sourceFile))
            {
                _logger.LogWarning("Recorded file not found: {File}", sourceFile);
                return Ok();
            }

            _logger.LogInformation("Found recorded FLV: {File}", sourceFile);

            // -------------------------------------------------------------
            // 2. Создаём папку назначения
            // -------------------------------------------------------------
            var targetDir = $"/var/www/streamplatform/media/users/{userId}/streams/{stream.Id}/";
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, "record.mp4");

            // -------------------------------------------------------------
            // 3. Конвертируем FLV → MP4
            // -------------------------------------------------------------
            var ffmpeg = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{sourceFile}\" -c copy \"{targetFile}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var ffmpegProcess = Process.Start(ffmpeg);
            ffmpegProcess.WaitForExit();

            if (ffmpegProcess.ExitCode != 0)
            {
                _logger.LogError("FFmpeg conversion failed. Exit code: {Code}", ffmpegProcess.ExitCode);
                return Ok();
            }

            _logger.LogInformation("Converted MP4 saved: {File}", targetFile);

            // -------------------------------------------------------------
            // 4. Права доступа
            // -------------------------------------------------------------
            Process.Start("chmod", $"-R 775 \"{targetDir}\"")?.WaitForExit();
            Process.Start("chown", $"-R boxedstream:boxedstream \"{targetDir}\"")?.WaitForExit();

            _logger.LogInformation("Permissions applied to {Dir}", targetDir);

            // -------------------------------------------------------------
            // 5. Сохраняем путь в БД
            // -------------------------------------------------------------
            stream.RecordPath = targetFile;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Stream END saved successfully. User {UserId}, Stream {StreamId}",
                userId, stream.Id
            );

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream END error for key: {Key}", name);
            return Ok(); // nginx always expects 200
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