using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services.NotificationService;
using System.Diagnostics;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{
    public interface IStreamService
    {
        Task<StreamModel> StartStreamAsync(int userId, string streamKey);
        Task<bool> EndStreamAsync(int userId, string streamKey);
        /// <summary>
        /// Immediately ends an active stream (no reconnect window), stops HLS transcoder and notifies viewers offline.
        /// Used when a stream_ban / full_ban is issued mid-broadcast.
        /// </summary>
        Task<bool> ForceTerminateActiveStreamAsync(int userId, string? reason = null);
        Task<StreamInfoDto?> GetStreamInfoAsync(int userId);
        Task<bool> ValidateStreamKeyAsync(string streamKey);
        Task<bool> IsUserStreamingAsync(int userId);
        Task<int> IncrementViewCountAsync(int streamId);
        Task<StreamModel?> GetStreamByUserIdAsync(int userId);
        Task UpdateStreamRecordPathAsync(int userId, string filePath);
        Task UpdateHeartbeatAsync(int userId, string? streamKey = null);
    }

    public class StreamService : IStreamService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StreamService> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IStreamLiveNotifier _streamLiveNotifier;
        private readonly IPlatformSanctionService _platformSanctions;

        private readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(30);
        private readonly string RecordsBase = "/var/www/streamplatform/records/";
        private readonly string MediaBase = "/var/www/streamplatform/media/users/";
        private readonly string HlsLiveBase = "/var/www/streamplatform/live/";

        public StreamService(AppDbContext context, 
                            INotificationRepository notificationRepository, 
                            INotificationSender notificationSender, 
                            ILogger<StreamService> logger,
                            IServiceScopeFactory scopeFactory,
                            IStreamLiveNotifier streamLiveNotifier,
                            IPlatformSanctionService platformSanctions
                            )
        {
            _context = context;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _streamLiveNotifier = streamLiveNotifier;
            _platformSanctions = platformSanctions;
        }

        public async Task<StreamModel?> GetActiveStreamForUserAsync(int userId)
        {
            return await _context.Streams
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<StreamModel> StartStreamAsync(int userId, string streamKey)
        {
            var now = DateTime.UtcNow;

            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new ArgumentException($"User {userId} not found");

            if (user.StreamKey != streamKey)
                throw new UnauthorizedAccessException("Stream key mismatch");

            if (await _platformSanctions.BlocksStreamingAsync(userId))
            {
                var message = await _platformSanctions.GetBlockMessageAsync(
                    userId,
                    PlatformSanctionTypes.StreamBan,
                    PlatformSanctionTypes.FullBan);
                throw new UnauthorizedAccessException(message ?? "Streaming is blocked by platform sanction");
            }


            // --- 1. Если есть активный стрим — обновляем пинг ---
            var active = await GetActiveStreamForUserAsync(userId);
            if (active != null)
            {
                active.LastPingAt = now;
                EnsurePlaybackId(active);
                user.CurrentStream = active;
                user.IsOnline = true;

                await _context.SaveChangesAsync();
                TryEnsureHlsPlaybackLink(streamKey, active.PublicId);
                await NotifyStreamLiveAsync(userId);
                return active;
            }


            // --- 2. Возврат в окно реконнекта ---
            var last = await _context.Streams
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (last != null && last.EndedAt != null && (now - last.EndedAt.Value) <= ReconnectWindow)
            {
                last.EndedAt = null;
                last.LastPingAt = now;
                EnsurePlaybackId(last);

                user.CurrentStream = last;
                user.IsOnline = true;

                await _context.SaveChangesAsync();
                TryEnsureHlsPlaybackLink(streamKey, last.PublicId);
                await NotifyStreamLiveAsync(userId);
                return last;
            }


            // --- 3. Создаём новый стрим ---
            var stream = new StreamModel
            {
                UserId = userId,
                StreamName = user.LastStreamName ?? $"{user.Nickname}'s Stream",
                CategoryId = user.LastCategoryId,
                Tags = new List<StreamTagModel>(),
                PreviewUrl = user.LastPreviewUrl,
                StartedAt = now,
                TotalViews = 0,
                RecordEnabled = user.RecordEnabled,  // здесь важно
                LastPingAt = now,
                PublicId = Guid.NewGuid().ToString()
            };

            _context.Streams.Add(stream);
            await _context.SaveChangesAsync();


            // --- 4. Подготовка директорий (только если запись включена) ---
            if (user.RecordEnabled)
            {
                // nginx пишет во /var/www/streamplatform/records — backend туда НЕ лезет.

                // backend создаёт только свою структуру хранения
                var targetDir = Path.Combine(MediaBase, userId.ToString(), "streams", stream.Id.ToString());
                Directory.CreateDirectory(targetDir);

                stream.RecordPath = Path.Combine(targetDir, "record.mp4");
            }
            else
            {
                stream.RecordPath = null;
            }


            // --- 5. Привязка стрима к пользователю ---
            user.CurrentStream = stream;
            user.IsOnline = true;
            await _context.SaveChangesAsync();

            await UpdateStreamTagsAsync(stream, user.LastTags);
            await _context.SaveChangesAsync();

            //await _liveTranscoder.StartAsync(stream, user);

            // --- 6. Отправка уведомлений подписчикам ---
            var subscribers = await _context.Subscriptions
                .Where(s => s.TargetUserId == userId && s.IsActive)
                .Include(s => s.Subscriber)
                .Select(s => s.Subscriber)
                .ToListAsync();

            var payload = new
            {
                StreamId = stream.Id,
                StreamerId = user.Id,
                StreamerName = user.Nickname,
                StreamName = stream.StreamName
            };

            await _notificationSender.NotifyStreamerSubscribersAsync(
                subscribers,
                user.Id,
                payload,
                NotificationType.StreamStarted
            );

            TryCleanHlsOutput(streamKey, stream.PublicId);
            TryEnsureHlsPlaybackLink(streamKey, stream.PublicId);
            await NotifyStreamLiveAsync(userId);

            return stream;
        }


        public async Task UpdateHeartbeatAsync(int userId, string? streamKey = null)
        {
            var stream = await _context.Streams
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (stream == null) return;

            stream.LastPingAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> ForceTerminateActiveStreamAsync(int userId, string? reason = null)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Tags)
                    .ThenInclude(t => t.Tag)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return false;

            var streamKey = user.StreamKey;
            var stream = await _context.Streams
                .Include(s => s.Tags)
                    .ThenInclude(t => t.Tag)
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (stream == null)
            {
                var changed = false;
                if (user.IsOnline || user.CurrentStream != null)
                {
                    user.IsOnline = false;
                    user.CurrentStream = null;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(streamKey))
                {
                    TryKillHlsTranscoder(streamKey);
                    TryCleanHlsOutput(streamKey);
                }

                if (changed)
                {
                    await _context.SaveChangesAsync();
                    await _streamLiveNotifier.PublishStatusAsync(userId, null);
                }

                return false;
            }

            stream.WaitingReconnect = false;
            stream.EndedAt = DateTime.UtcNow;
            user.IsOnline = false;
            user.LastStreamName = stream.StreamName;
            user.LastTags = stream.Tags.Select(st => st.Tag.Slug).ToList();
            user.LastCategoryId = stream.CategoryId;
            user.LastPreviewUrl = stream.PreviewUrl;
            user.CurrentStream = null;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(streamKey))
            {
                TryKillHlsTranscoder(streamKey);
                TryCleanHlsOutput(streamKey, stream.PublicId);
            }

            await _streamLiveNotifier.PublishStatusAsync(userId, null);

            _logger.LogWarning(
                "Force-terminated stream {StreamId} for user {UserId} ({Reason})",
                stream.Id, userId, reason ?? "unspecified");

            try
            {
                if (stream.RecordEnabled && !string.IsNullOrEmpty(stream.RecordPath) && !string.IsNullOrWhiteSpace(streamKey))
                {
                    _ = ProcessRecordingAsync(streamKey, stream.RecordPath, stream.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process recording after force-terminate for stream {Id}", stream.Id);
            }

            return true;
        }

        public async Task<bool> EndStreamAsync(int userId, string streamKey)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || user.StreamKey != streamKey)
                return false;

            var stream = await GetActiveStreamForUserAsync(userId);

            // Если стрим уже завершён ранее — просто почистим флаги
            if (stream == null)
            {
                user.IsOnline = false;
                user.CurrentStream = null;
                TryKillHlsTranscoder(streamKey);
                TryCleanHlsOutput(streamKey);
                await _context.SaveChangesAsync();
                return true;
            }

            // ===============================
            // 🚧 Фаза ожидания реконнекта
            // ===============================
            stream.WaitingReconnect = true;
            stream.LastPingAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Запускаем фоновую задержку реконнекта
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30)); // окно реконнекта

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var s = await db.Streams
                    .Include(x => x.User)
                    .Include(x => x.Tags)
                        .ThenInclude(t => t.Tag)
                    .FirstOrDefaultAsync(x => x.Id == stream.Id);

                if (s == null)
                    return;

                // Если стрим уже продолжился — выходим
                if (!s.WaitingReconnect)
                    return;

                // Если был пинг < 25 секунд назад — считаем, что стрим активен
                if (s.LastPingAt.HasValue &&
                    (DateTime.UtcNow - s.LastPingAt.Value).TotalSeconds < 25)
                {
                    s.WaitingReconnect = false;
                    await db.SaveChangesAsync();
                    return;
                }

                // ===============================
                // ❌ Реальное завершение стрима
                // ===============================
                var usr = s.User;

                //await _liveTranscoder.StopAsync(s.Id);

                s.EndedAt = DateTime.UtcNow;
                usr.IsOnline = false;
                usr.LastStreamName = s.StreamName;
                usr.LastTags = s.Tags.Select(st => st.Tag.Slug).ToList();
                usr.LastCategoryId = s.CategoryId;
                usr.LastPreviewUrl = s.PreviewUrl;
                usr.CurrentStream = null;

                s.WaitingReconnect = false;
                await db.SaveChangesAsync();

                TryCleanHlsOutput(streamKey, s.PublicId);

                var liveNotifier = scope.ServiceProvider.GetRequiredService<IStreamLiveNotifier>();
                await liveNotifier.PublishStatusAsync(usr.Id, null);

                // Обработка записи
                try
                {
                    if (s.RecordEnabled && !string.IsNullOrEmpty(s.RecordPath))
                    {
                        await ProcessRecordingAsync(streamKey, s.RecordPath, s.Id);
                    }
                }
                catch (Exception ex)
                {
                    // Логируем, но не ломаем завершение
                    _logger.LogError(ex, "Failed to process recording for stream {Id}", s.Id);
                }

            });

            // OBS считает END подтверждённым
            return true;
        }



        private Task ProcessRecordingAsync(string streamKey, string targetFile, int streamId)
        {
            return Task.Run(() =>
            {
                try
                {
                    var srcDir = RecordsBase;

                    _logger.LogInformation(
                        "Recording processing started. StreamId={StreamId}, StreamKey={StreamKey}",
                        streamId, streamKey
                    );

                    if (!Directory.Exists(srcDir))
                    {
                        _logger.LogError("Records base directory not found: {Dir}", srcDir);
                        return;
                    }

                    var flvs = Directory
                        .GetFiles(srcDir, $"{streamKey}*.flv")
                        .OrderBy(f => File.GetCreationTimeUtc(f))
                        .ToArray();

                    if (flvs.Length == 0)
                    {
                        _logger.LogWarning(
                            "No flv files found for stream {StreamKey} in {Dir}",
                            streamKey, srcDir
                        );
                        return;
                    }

                    _logger.LogInformation(
                        "Found {Count} flv files for stream {StreamId}",
                        flvs.Length, streamId
                    );

                    // ===============================
                    // ⏳ Ждём завершения записи nginx
                    // ===============================
                    foreach (var f in flvs)
                    {
                        if (!WaitForFileStabilization(f))
                        {
                            _logger.LogWarning("File not stabilized: {File}", f);
                            return;
                        }
                    }

                    // ===============================
                    // 📁 Подготовка папки назначения
                    // ===============================
                    var targetDir = Path.GetDirectoryName(targetFile)!;
                    Directory.CreateDirectory(targetDir);

                    var concatFile = Path.Combine(targetDir, $"concat_{streamId}.txt");
                    using (var sw = new StreamWriter(concatFile))
                    {
                        foreach (var f in flvs)
                            sw.WriteLine($"file '{f.Replace("'", "'\\''")}'");
                    }

                    // ===============================
                    // 🎬 Склейка через ffmpeg
                    // ===============================
                    if (!RunFfmpeg(
                        $"-y -f concat -safe 0 -i \"{concatFile}\" -c copy \"{targetFile}\"",
                        out var concatError))
                    {
                        _logger.LogError(
                            "ffmpeg concat failed for stream {StreamId}: {Error}",
                            streamId, concatError
                        );

                        // fallback — последний flv
                        var last = flvs.Last();
                        if (!RunFfmpeg(
                            $"-y -i \"{last}\" -c copy \"{targetFile}\"",
                            out var fallbackError))
                        {
                            _logger.LogError(
                                "ffmpeg fallback failed for stream {StreamId}: {Error}",
                                streamId, fallbackError
                            );
                            return;
                        }
                    }

                    _logger.LogInformation(
                        "Recording successfully saved: {File}",
                        targetFile
                    );

                    // ===============================
                    // 🧹 Очистка временных flv
                    // ===============================
                    foreach (var f in flvs)
                        TryDeleteFile(f);

                    TryDeleteFile(concatFile);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Recording processing failed for stream {StreamId}",
                        streamId
                    );
                }
            });
        }


        private bool WaitForFileStabilization(string path, int attempts = 10, int delayMs = 1000)
        {
            try
            {
                long lastSize = -1;

                for (int i = 0; i < attempts; i++)
                {
                    if (!File.Exists(path))
                        return false;

                    var size = new FileInfo(path).Length;
                    if (size == lastSize)
                        return true;

                    lastSize = size;
                    Thread.Sleep(delayMs);
                }
            }
            catch { }

            return false;
        }

        private bool RunFfmpeg(string args, out string error)
        {
            error = string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/ffmpeg",
                    Arguments = args,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p == null)
                {
                    error = "Failed to start ffmpeg process";
                    return false;
                }

                error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {File}", path);
            }
        }




        public async Task<StreamInfoDto?> GetStreamInfoAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Category)
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Tags)
                    .ThenInclude(st => st.Tag)
                .FirstOrDefaultAsync(u => u.Id == userId);

            var stream = user?.CurrentStream;
            if (stream == null || stream.EndedAt != null) return null;

            var changed = false;
            if (string.IsNullOrWhiteSpace(stream.PublicId))
            {
                EnsurePlaybackId(stream);
                changed = true;
            }
            if (changed)
                await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(user!.StreamKey))
                TryEnsureHlsPlaybackLink(user.StreamKey, stream.PublicId);

            return MapStreamInfo(user, stream);
        }

        public async Task<bool> ValidateStreamKeyAsync(string streamKey)
        {
            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return false;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.StreamKey == streamKey);
            if (user == null) return false;
            if (await _platformSanctions.BlocksStreamingAsync(userId)) return false;
            return true;
        }

        public async Task<bool> IsUserStreamingAsync(int userId)
        {
            var user = await _context.Users.Include(u => u.CurrentStream).FirstOrDefaultAsync(u => u.Id == userId);
            return user?.IsOnline == true && user.CurrentStream?.EndedAt == null;
        }

        public async Task<int> IncrementViewCountAsync(int streamId)
        {
            var stream = await _context.Streams.FindAsync(streamId);
            if (stream == null) return 0;
            stream.TotalViews++;
            await _context.SaveChangesAsync();
            return stream.TotalViews;
        }

        public async Task<StreamModel?> GetStreamByUserIdAsync(int userId)
        {
            return await _context.Streams.Include(s => s.User).FirstOrDefaultAsync(s => s.UserId == userId && s.EndedAt == null);
        }

        private bool TryParseUserIdFromStreamKey(string streamKey, out int userId)
        {
            userId = 0;
            if (string.IsNullOrEmpty(streamKey) || !streamKey.StartsWith("live_")) return false;
            var parts = streamKey.Split('_', 3);
            return parts.Length >= 2 && int.TryParse(parts[1], out userId);
        }

        public async Task UpdateStreamRecordPathAsync(int userId, string filePath)
        {
            try
            {
                var stream = await _context.Streams.FirstOrDefaultAsync(s => s.UserId == userId && s.StartedAt != null && s.EndedAt == null);
                if (stream == null) return;
                stream.RecordPath = filePath;
                stream.EndedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while updating record path for user {UserId}", userId);
                throw;
            }
        }

        public async Task<StreamInfoDto?> GetStreamInfoByIdAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Category)
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Tags)
                    .ThenInclude(st => st.Tag)
                .FirstOrDefaultAsync(u => u.Id == userId);

            var stream = user?.CurrentStream;
            if (stream == null || stream.EndedAt != null) return null;

            return MapStreamInfo(user!, stream);
        }

        private async Task NotifyStreamLiveAsync(int userId)
        {
            var info = await GetStreamInfoAsync(userId);
            await _streamLiveNotifier.PublishStatusAsync(userId, info);

            if (info?.IsLive == true)
            {
                _streamLiveNotifier.ScheduleRepublish(
                    userId,
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(8),
                    TimeSpan.FromSeconds(15));
            }
        }

        /// <summary>
        /// Public playback path uses PublicId; on-disk ffmpeg output stays under StreamKey.
        /// Symlink: live/{PublicId} → live/{StreamKey} so viewers never see the publish key.
        /// </summary>
        private void TryEnsureHlsPlaybackLink(string streamKey, string? playbackId)
        {
            if (string.IsNullOrWhiteSpace(streamKey) || string.IsNullOrWhiteSpace(playbackId))
                return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(streamKey, @"^[A-Za-z0-9_-]+$"))
                return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(playbackId, @"^[A-Za-z0-9_-]+$"))
                return;

            try
            {
                Directory.CreateDirectory(HlsLiveBase);
                var linkPath = Path.Combine(HlsLiveBase, playbackId);
                if (Directory.Exists(linkPath) && !IsSymlink(linkPath))
                    return;
                TryUnlinkPlaybackPath(linkPath);

                // Relative symlink so rename of live/ root still works
                Directory.CreateSymbolicLink(linkPath, streamKey);
                _logger.LogInformation("HLS playback link {PlaybackId} -> {StreamKey}", playbackId, MaskKey(streamKey));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create HLS playback link for {PlaybackId}", playbackId);
            }
        }

        private void TryCleanHlsOutput(string streamKey, string? playbackId = null)
        {
            if (string.IsNullOrWhiteSpace(streamKey) && string.IsNullOrWhiteSpace(playbackId))
                return;
            if (!string.IsNullOrWhiteSpace(streamKey) && !IsSafeHlsName(streamKey))
                return;
            if (!string.IsNullOrWhiteSpace(playbackId) && !IsSafeHlsName(playbackId))
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(playbackId))
                    TryUnlinkPlaybackPath(Path.Combine(HlsLiveBase, playbackId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove HLS playback link {PlaybackId}", playbackId);
            }

            if (string.IsNullOrWhiteSpace(streamKey))
                return;

            // ffmpeg writes as www-data into 2755 dirs — boxedstream cannot unlink those files.
            // Reuse the existing passwordless root helper (pkill + rm -rf). Harmless at stream start
            // because nginx exec_push starts ffmpeg only after on_publish / StartStream returns.
            if (TryRunProcess(
                    "/usr/bin/sudo",
                    $"-n /usr/local/bin/streamplatform-kill-hls-transcoder {streamKey}",
                    out var sudoExit) && sudoExit == 0)
            {
                _logger.LogInformation("Cleaned HLS output via sudo helper for {StreamKey}", MaskKey(streamKey));
                return;
            }

            if (TryRunProcess(
                    "/usr/bin/sudo",
                    $"-n /usr/local/bin/streamplatform-clean-hls {streamKey}",
                    out var cleanExit) && cleanExit == 0)
            {
                _logger.LogInformation("Cleaned HLS output via clean helper for {StreamKey}", MaskKey(streamKey));
                return;
            }

            try
            {
                var dir = Path.Combine(HlsLiveBase, streamKey);
                if (IsSymlink(dir))
                    File.Delete(dir);
                else if (Directory.Exists(dir))
                    DeleteDirectoryUnlink(dir);

                _logger.LogInformation("Cleaned stale HLS output for {StreamKey}", MaskKey(streamKey));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean HLS output");
            }
        }

        /// <summary>
        /// Unlink files/dirs without chmod. Directory.Delete(recursive) fails on ffmpeg files
        /// owned by www-data even when the backend is in that group.
        /// </summary>
        private static void DeleteDirectoryUnlink(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                File.Delete(file);

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (IsSymlink(sub))
                    File.Delete(sub);
                else
                    DeleteDirectoryUnlink(sub);
            }

            Directory.Delete(dir);
        }

        private static void TryUnlinkPlaybackPath(string linkPath)
        {
            if (IsSymlink(linkPath) || File.Exists(linkPath))
                File.Delete(linkPath);
        }

        private static bool IsSafeHlsName(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_-]+$");

        private static void EnsurePlaybackId(StreamModel stream)
        {
            if (string.IsNullOrWhiteSpace(stream.PublicId))
                stream.PublicId = Guid.NewGuid().ToString();
        }

        private static string GetPlaybackId(StreamModel stream) =>
            string.IsNullOrWhiteSpace(stream.PublicId) ? stream.Id.ToString() : stream.PublicId;

        private static bool IsSymlink(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 10)
                return "***";
            return key[..8] + "…";
        }

        /// <summary>
        /// Stops the ffmpeg HLS transcoder for this publish key so viewers lose the playlist immediately.
        /// OBS may stay connected to nginx-rtmp until it disconnects; a new publish is blocked by sanction checks.
        /// ffmpeg runs as www-data, so plain pkill from boxedstream often fails — prefer CAP_KILL on the service
        /// or sudoers helper /usr/local/bin/streamplatform-kill-hls-transcoder.
        /// </summary>
        private void TryKillHlsTranscoder(string streamKey)
        {
            if (string.IsNullOrWhiteSpace(streamKey))
                return;

            // Stream keys are generated as live_{id}_{hex} — reject anything unexpected before shelling out.
            if (!System.Text.RegularExpressions.Regex.IsMatch(streamKey, @"^[A-Za-z0-9_-]+$"))
            {
                _logger.LogWarning("Refusing to kill transcoder for unsafe stream key");
                return;
            }

            // 1) Passwordless helper (see scripts/sudoers-streamplatform-kill-hls)
            if (TryRunProcess(
                    "/usr/bin/sudo",
                    $"-n /usr/local/bin/streamplatform-kill-hls-transcoder {streamKey}",
                    out var sudoExit))
            {
                if (sudoExit == 0)
                {
                    _logger.LogInformation("HLS transcoder killed via sudo helper for {StreamKey}", streamKey);
                    return;
                }
            }

            // 2) Direct pkill (works when backend has CAP_KILL or runs as same user as ffmpeg)
            if (TryRunProcess("/usr/bin/pkill", $"-f ffmpeg.*{streamKey}", out var pkillExit))
            {
                // 0 = matched, 1 = no process
                if (pkillExit is 0 or 1)
                {
                    _logger.LogInformation(
                        "HLS transcoder pkill for {StreamKey} finished with exit {ExitCode}",
                        streamKey, pkillExit);
                    return;
                }

                _logger.LogWarning(
                    "pkill could not stop transcoder for {StreamKey} (exit {ExitCode}). " +
                    "Grant CAP_KILL to streamplatform-backend or install scripts/sudoers-streamplatform-kill-hls",
                    streamKey, pkillExit);
                return;
            }

            _logger.LogWarning("Failed to invoke pkill for HLS transcoder {StreamKey}", streamKey);
        }

        private static bool TryRunProcess(string fileName, string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return false;
                }

                exitCode = process.ExitCode;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static StreamInfoDto MapStreamInfo(UserModel user, StreamModel stream)
        {
            return new StreamInfoDto
            {
                StreamId = stream.Id,
                StreamName = stream.StreamName,
                StreamerName = user.Nickname,
                StreamerId = user.Id,
                Tags = stream.Tags.Select(st => st.Tag.Name).ToList(),
                CategoryId = stream.CategoryId,
                CategoryName = stream.Category?.Name,
                CategoryBannerImageUrl = stream.Category?.BannerImageUrl,
                StreamLanguage = user.StreamLanguage,
                PreviewUrl = stream.PreviewUrl,
                HlsUrl = $"/hls/{GetPlaybackId(stream)}/master.m3u8",
                TotalViews = stream.TotalViews,
                StartedAt = stream.StartedAt,
                EndedAt = stream.EndedAt,
                IsLive = stream.EndedAt == null,
                Title = stream.StreamName
            };
        }

        private async Task UpdateStreamTagsAsync(StreamModel stream, List<string>? tagSlugs)
        {
            tagSlugs ??= new List<string>();

            // привести к нижнему регистру и убрать пустые/дубли
            tagSlugs = tagSlugs
                .Select(s => s.Trim().ToLower())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();

            // существующие теги в БД
            var existingTags = await _context.Tags
                .Where(t => tagSlugs.Contains(t.Slug))
                .ToListAsync();

            // создать новые, если не существуют
            var missingSlugs = tagSlugs.Except(existingTags.Select(t => t.Slug)).ToList();
            foreach (var slug in missingSlugs)
            {
                var newTag = new TagModel
                {
                    Name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug),
                    Slug = slug
                };

                _context.Tags.Add(newTag);
                existingTags.Add(newTag);
            }

            await _context.SaveChangesAsync();

            // текущие связи
            var currentTagIds = stream.Tags.Select(t => t.TagId).ToList();
            var targetTagIds = existingTags.Select(t => t.Id).ToList();

            // добавляем новые связи
            var toAdd = targetTagIds.Except(currentTagIds);
            foreach (var tagId in toAdd)
            {
                stream.Tags.Add(new StreamTagModel
                {
                    StreamId = stream.Id,
                    TagId = tagId
                });
            }

            // удаляем лишние
            var toRemove = currentTagIds.Except(targetTagIds);
            stream.Tags = new HashSet<StreamTagModel>(stream.Tags
                .Where(st => !toRemove.Contains(st.TagId)));
        }




    }
}
