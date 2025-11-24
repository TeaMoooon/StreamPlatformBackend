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
    /*[HttpPost("end")]
    public async Task<IActionResult> OnStreamEnd([FromForm] string name)
    {
        try
        {
            _logger.LogInformation("=== STREAM END CALLBACK === Raw key: {Key}", name);

            if (string.IsNullOrEmpty(name))
                return Ok();

            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Invalid stream key format on END: {Key}", name);
                return Ok();
            }

            // Завершаем стрим через сервис (только ставим EndedAt)
            await _streamService.EndStreamAsync(userId, name);

            // Получаем объект завершенного стрима
            var stream = await _context.Streams
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EndedAt != null && s.RecordEnabled);

            if (stream == null)
            {
                _logger.LogWarning("No ended stream found for user {UserId}", userId);
                return Ok();
            }

            // -------------------------------------------------------------
            // 1. ИЩЕМ ФАЙЛ ЗАПИСИ
            // -------------------------------------------------------------
            var sourceDir = "/var/www/streamplatform/records/";

            var files = Directory.GetFiles(sourceDir, "*.flv");
            if (files.Length == 0)
            {
                _logger.LogWarning("No FLV files found in records dir");
                return Ok();
            }

                // Находим последний записанный файл
                var newestFile = files
            .OrderByDescending(f => System.IO.File.GetCreationTimeUtc(f))
            .First();

                _logger.LogInformation("Found recorded FLV: {File}", newestFile);

            // -------------------------------------------------------------
            // 2. ГОТОВИМ ЦЕЛЕВУЮ ПАПКУ
            // -------------------------------------------------------------
            var targetDir = $"/var/www/streamplatform/media/users/{userId}/streams/{stream.Id}/";
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, "record.mp4");

            // -------------------------------------------------------------
            // 3. КОНВЕРТИРУЕМ FLV → MP4
            // -------------------------------------------------------------
            var ffmpeg = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{newestFile}\" -c copy \"{targetFile}\"",
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
                return Ok(); // не ломаем nginx
            }

            _logger.LogInformation("Converted MP4 saved: {File}", targetFile);

            // -------------------------------------------------------------
            // 4. ПРАВА ДОСТУПА
            // -------------------------------------------------------------
            Process.Start("chmod", $"-R 775 \"{targetDir}\"")?.WaitForExit();
            Process.Start("chown", $"-R boxedstream:boxedstream \"{targetDir}\"")?.WaitForExit();

            _logger.LogInformation("Permissions applied to {Dir}", targetDir);

            // -------------------------------------------------------------
            // 5. СОХРАНЯЕМ ПУТЬ В БАЗЕ
            // -------------------------------------------------------------
            stream.RecordPath = targetFile;
            await _context.SaveChangesAsync(); // сохраняем напрямую

            _logger.LogInformation("Stream finished and saved: User {UserId}, Stream {StreamId}", userId, stream.Id);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream END error for key: {Key}", name);
            return Ok(); // nginx must always get 200
        }
    }*/

    [HttpPost("end")]
    public async Task<IActionResult> OnStreamEnd([FromForm] string name)
    {
        try
        {
            _logger.LogInformation("=== STREAM END CALLBACK === Raw key: {Key}", name);

            if (string.IsNullOrEmpty(name))
                return Ok();

            if (!TryParseUserIdFromStreamKey(name, out int userId))
            {
                _logger.LogWarning("Invalid stream key format on END: {Key}", name);
                return Ok();
            }

            // Ищем активный стрим для пользователя (не важно RecordEnabled)
            var stream = await _context.Streams
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt) // последний запущенный
                .FirstOrDefaultAsync();

            if (stream == null)
            {
                _logger.LogWarning("No active stream found for user {UserId}", userId);
                return Ok();
            }

            // Устанавливаем EndedAt
            stream.EndedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Stream marked as ended: User {UserId}, Stream {StreamId}", userId, stream.Id);

            // -------------------------------------------------------------
            // 1. ИЩЕМ ФАЙЛ ЗАПИСИ
            // -------------------------------------------------------------
            var sourceDir = "/var/www/streamplatform/records/";
            var files = Directory.GetFiles(sourceDir, "*.flv");


            if (!Directory.Exists(sourceDir))
            {
                _logger.LogWarning("Records directory not found, creating: {Dir}", sourceDir);
                Directory.CreateDirectory(sourceDir);
            }

            // Находим последний записанный файл
            var newestFile = files.OrderByDescending(f => System.IO.File.GetCreationTimeUtc(f)).First();
            _logger.LogInformation("Found recorded FLV: {File}", newestFile);

            // -------------------------------------------------------------
            // 2. ГОТОВИМ ЦЕЛЕВУЮ ПАПКУ
            // -------------------------------------------------------------
            var targetDir = $"/var/www/streamplatform/media/users/{userId}/streams/{stream.Id}/";
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, "record.mp4");

            // -------------------------------------------------------------
            // 3. КОНВЕРТИРУЕМ FLV → MP4
            // -------------------------------------------------------------
            var ffmpeg = new ProcessStartInfo
            {
                FileName = "/usr/bin/ffmpeg",
                Arguments = $"-y -i \"{newestFile}\" -c copy \"{targetFile}\"",
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
                return Ok(); // не ломаем nginx
            }

            _logger.LogInformation("Converted MP4 saved: {File}", targetFile);

            // -------------------------------------------------------------
            // 4. ПРАВА ДОСТУПА
            // -------------------------------------------------------------
            Process.Start("chmod", $"-R 775 \"{targetDir}\"")?.WaitForExit();
            Process.Start("chown", $"-R boxedstream:boxedstream \"{targetDir}\"")?.WaitForExit();

            _logger.LogInformation("Permissions applied to {Dir}", targetDir);

            // -------------------------------------------------------------
            // 5. СОХРАНЯЕМ ПУТЬ В БАЗЕ
            // -------------------------------------------------------------
            stream.RecordPath = targetFile;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Stream finished and saved: User {UserId}, Stream {StreamId}", userId, stream.Id);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream END error for key: {Key}", name);
            return Ok(); // nginx must always get 200
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