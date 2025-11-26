using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services.NotificationService;
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

    }

    public class StreamService : IStreamService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StreamService> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;

        public StreamService(AppDbContext context,INotificationRepository notificationRepository,INotificationSender notificationSender,ILogger<StreamService> logger)
        {
            _context = context;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
        }

        public async Task<StreamModel> StartStreamAsync(int userId, string streamKey)
        {
            // Получаем пользователя вместе с текущим стримом
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new ArgumentException($"User with ID {userId} not found");
            if (user.StreamKey != streamKey)
                throw new UnauthorizedAccessException("Invalid stream key");

            // Если уже есть активный стрим — возвращаем его
            if (user.CurrentStream != null && user.CurrentStream.EndedAt == null)
                return user.CurrentStream;

            // Создаём новый стрим
            var stream = new StreamModel
            {
                UserId = user.Id,
                StreamName = user.LastStreamName ?? $"{user.Nickname}'s Stream",
                Tags = user.LastTags ?? new List<string>(),
                PreviewUrl = user.LastPreviewUrl,
                StartedAt = DateTime.UtcNow,
                TotalViews = 0,

                // Подтягиваем настройку пользователя
                RecordEnabled = user.RecordEnabled
            };
            // Добавляем стрим в контекст, чтобы EF присвоил Id
            _context.Streams.Add(stream);
            await _context.SaveChangesAsync(); // теперь stream.Id реально присвоен

            if (user.RecordEnabled)
{
                var recordDir = $"/var/www/streamplatform/media/users/{userId}/streams/{stream.Id}/";

                try
                {
                    if (!Directory.Exists(recordDir))
                        Directory.CreateDirectory(recordDir);

                    // chmod 775
                    var chmod = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"-R 775 \"{recordDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(chmod)?.WaitForExit();

                    // chown на пользователя приложения
                    var chown = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chown",
                        Arguments = $"-R boxedstream:boxedstream \"{recordDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(chown)?.WaitForExit();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при создании папки для записи стрима {StreamId}", stream.Id);
                    stream.RecordEnabled = false; // чтобы не пытаться записывать
                }

                stream.RecordPath = Path.Combine(recordDir, "record.mp4");
                stream.RecordEnabled = true; // активируем запись
            }



            user.CurrentStream = stream;
            user.IsOnline = true;

            await _context.SaveChangesAsync();

            // Получаем подписчиков стримера
            var subscribers = await _context.Subscriptions
                .Where(s => s.TargetUserId == userId)
                .Include(s => s.Subscriber)
                .Select(s => s.Subscriber)
                .ToListAsync();

            // Формируем payload уведомления
            var payload = new
            {
                StreamId = stream.Id,
                StreamerId = user.Id,
                StreamerName = user.Nickname,
                StreamName = stream.StreamName
            };

            // Отправляем уведомления через NotificationSender
            await _notificationSender.NotifyStreamerSubscribersAsync(subscribers, user.Id, payload, NotificationType.StreamStarted);

            return stream;
        }




        public async Task<bool> EndStreamAsync(int userId, string streamKey)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.CurrentStream == null || user.StreamKey != streamKey)
                return false;

            user.CurrentStream.EndedAt = DateTime.UtcNow;
            user.IsOnline = false;

            // Сохраняем последние настройки
            user.LastStreamName = user.CurrentStream.StreamName;
            user.LastTags = user.CurrentStream.Tags;
            user.LastPreviewUrl = user.CurrentStream.PreviewUrl;

            // Обнуляем текущий стрим, чтобы пользователь был "не в эфире"
            user.CurrentStream = null;

            await _context.SaveChangesAsync();

            // 🔔 Уведомления о завершении стрима подписчикам
            //await _notificationService.NotifyStreamEndedAsync(user.CurrentStream);

            return true;
        }


        public async Task<bool> UpdateStreamAsync(int userId, StreamUpdateDto updateDto)
        {
            var stream = (await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId))
                ?.CurrentStream;

            if (stream == null) return false;

            if (!string.IsNullOrEmpty(updateDto.StreamName))
                stream.StreamName = updateDto.StreamName;

            if (updateDto.Tags != null)
                stream.Tags = updateDto.Tags;

            if (!string.IsNullOrEmpty(updateDto.PreviewUrl))
                stream.PreviewUrl = updateDto.PreviewUrl;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<StreamInfoDto?> GetStreamInfoAsync(int userId)
        {
            var user = await _context.Users.Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

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
            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return false;

            return await _context.Users.AnyAsync(u => u.Id == userId && u.StreamKey == streamKey);
        }

        public async Task<bool> IsUserStreamingAsync(int userId)
        {
            var user = await _context.Users.Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

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
            return await _context.Streams.Include(s => s.User)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EndedAt == null);
        }

        private bool TryParseUserIdFromStreamKey(string streamKey, out int userId)
        {
            userId = 0;
            if (string.IsNullOrEmpty(streamKey) || !streamKey.StartsWith("live_")) return false;
            var parts = streamKey.Split('_');
            return parts.Length >= 2 && int.TryParse(parts[1], out userId);
        }


        public async Task UpdateStreamRecordPathAsync(int userId, string filePath)
        {
            try
            {
                _logger.LogInformation(
                    "Updating record path for user {UserId}: {Path}",
                    userId, filePath
                );

                var stream = await _context.Streams
                    .FirstOrDefaultAsync(s =>
                        s.UserId == userId &&
                        s.StartedAt != null &&
                        s.EndedAt == null // IsLive
                    );

                if (stream == null)
                {
                    _logger.LogWarning("No live stream found for user {UserId}", userId);
                    return;
                }

                stream.RecordPath = filePath;
                stream.EndedAt = DateTime.UtcNow; // запись завершена -> стрим завершён

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Record path saved successfully for live stream {StreamId}",
                    stream.Id
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error while updating record path for user {UserId}. File: {Path}",
                    userId, filePath);
                throw;
            }
        }

    }
}
