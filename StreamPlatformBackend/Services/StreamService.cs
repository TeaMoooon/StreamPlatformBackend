using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        Task<bool> UpdateStreamAsync(int userId, StreamUpdateDto updateDto);
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

        private readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(30);
        private readonly string RecordsBase = "/var/www/streamplatform/records/";
        private readonly string MediaBase = "/var/www/streamplatform/media/users/";

        public StreamService(AppDbContext context, 
                            INotificationRepository notificationRepository, 
                            INotificationSender notificationSender, 
                            ILogger<StreamService> logger,
                            IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
            _scopeFactory = scopeFactory;
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


            // --- 1. Если есть активный стрим — обновляем пинг ---
            var active = await GetActiveStreamForUserAsync(userId);
            if (active != null)
            {
                active.LastPingAt = now;
                user.CurrentStream = active;
                user.IsOnline = true;

                await _context.SaveChangesAsync();
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

                user.CurrentStream = last;
                user.IsOnline = true;

                await _context.SaveChangesAsync();
                return last;
            }


            // --- 3. Создаём новый стрим ---
            var stream = new StreamModel
            {
                UserId = userId,
                StreamName = user.LastStreamName ?? $"{user.Nickname}'s Stream",
                Tags = user.LastTags ?? new List<string>(),
                PreviewUrl = user.LastPreviewUrl,
                StartedAt = now,
                TotalViews = 0,
                RecordEnabled = user.RecordEnabled,  // здесь важно
                LastPingAt = now
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


            // --- 6. Отправка уведомлений подписчикам ---
            var subscribers = await _context.Subscriptions
                .Where(s => s.TargetUserId == userId)
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

                s.EndedAt = DateTime.UtcNow;
                usr.IsOnline = false;
                usr.LastStreamName = s.StreamName;
                usr.LastTags = s.Tags;
                usr.LastPreviewUrl = s.PreviewUrl;
                usr.CurrentStream = null;

                s.WaitingReconnect = false;
                await db.SaveChangesAsync();

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



        private async Task ProcessRecordingAsync(string streamKey, string targetFile, int streamId)
        {
            await Task.Run(() =>
            {
                try
                {
                    var srcDir = Path.Combine(RecordsBase, streamKey);
                    if (!Directory.Exists(srcDir)) return;

                    var flvs = Directory.GetFiles(srcDir, "*.flv").OrderBy(f => File.GetCreationTimeUtc(f)).ToArray();
                    if (flvs.Length == 0) return;

                    var targetDir = Path.GetDirectoryName(targetFile);
                    Directory.CreateDirectory(targetDir);

                    var concatFile = Path.Combine(targetDir, $"concat_{streamId}.txt");
                    using (var sw = new StreamWriter(concatFile))
                        foreach (var f in flvs) sw.WriteLine($"file '{f.Replace("'", "'\\''")}'");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/ffmpeg",
                        Arguments = $"-y -f concat -safe 0 -i \"{concatFile}\" -c copy \"{targetFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    var p = Process.Start(psi);
                    p.WaitForExit();

                    if (p.ExitCode != 0)
                    {
                        var last = flvs.Last();
                        var psi2 = new ProcessStartInfo
                        {
                            FileName = "/usr/bin/ffmpeg",
                            Arguments = $"-y -i \"{last}\" -c copy \"{targetFile}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        var p2 = Process.Start(psi2);
                        p2.WaitForExit();
                    }

                    foreach (var f in flvs) File.Delete(f);
                    if (Directory.GetFiles(srcDir).Length == 0) Directory.Delete(srcDir);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Recording processing failed for stream {StreamId}", streamId);
                }
            });
        }

        public async Task<bool> UpdateStreamAsync(int userId, StreamUpdateDto updateDto)
        {
            var stream = (await _context.Users.Include(u => u.CurrentStream).FirstOrDefaultAsync(u => u.Id == userId))?.CurrentStream;
            if (stream == null) return false;

            if (!string.IsNullOrEmpty(updateDto.StreamName)) stream.StreamName = updateDto.StreamName;
            if (updateDto.Tags != null) stream.Tags = updateDto.Tags;
            if (!string.IsNullOrEmpty(updateDto.PreviewUrl)) stream.PreviewUrl = updateDto.PreviewUrl;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<StreamInfoDto?> GetStreamInfoAsync(int userId)
        {
            var user = await _context.Users.Include(u => u.CurrentStream).FirstOrDefaultAsync(u => u.Id == userId);
            var stream = user?.CurrentStream;
            if (stream == null || stream.EndedAt != null) return null;

            return new StreamInfoDto
            {
                StreamId = stream.Id,
                StreamName = stream.StreamName,
                StreamerName = user.Nickname,
                StreamerId = user.Id,
                Tags = stream.Tags,
                PreviewUrl = stream.PreviewUrl,
                HlsUrl = $"/hls/{user.StreamKey}.m3u8",
                TotalViews = stream.TotalViews,
                StartedAt = stream.StartedAt,
                IsLive = stream.EndedAt == null
            };
        }

        public async Task<bool> ValidateStreamKeyAsync(string streamKey)
        {
            if (!TryParseUserIdFromStreamKey(streamKey, out int userId)) return false;
            return await _context.Users.AnyAsync(u => u.Id == userId && u.StreamKey == streamKey);
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
            var user = await _context.Users.Include(u => u.CurrentStream).FirstOrDefaultAsync(u => u.Id == userId);
            var stream = user?.CurrentStream;
            if (stream == null || stream.EndedAt != null) return null;

            return new StreamInfoDto
            {
                StreamId = stream.Id,
                StreamName = stream.StreamName,
                StreamerName = user.Nickname,
                StreamerId = user.Id,
                Tags = stream.Tags,
                PreviewUrl = stream.PreviewUrl,
                HlsUrl = $"/hls/{user.StreamKey}.m3u8",
                TotalViews = stream.TotalViews,
                StartedAt = stream.StartedAt,
                IsLive = stream.EndedAt == null
            };
        }


    }
}
