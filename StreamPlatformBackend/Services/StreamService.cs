using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;

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
    }

    public class StreamService : IStreamService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StreamService> _logger;
        private readonly IStreamNotificationService _notificationService;

        public StreamService(AppDbContext context, IStreamNotificationService notificationService, ILogger<StreamService> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<StreamModel> StartStreamAsync(int userId, string streamKey)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                throw new ArgumentException($"User with ID {userId} not found");

            if (user.StreamKey != streamKey)
                throw new UnauthorizedAccessException("Invalid stream key");

            if (user.CurrentStream != null && user.CurrentStream.EndedAt == null)
                return user.CurrentStream; // уже идёт стрим

            var stream = new StreamModel
            {
                UserId = user.Id,
                StreamName = user.LastStreamName ?? $"{user.Nickname}'s Stream",
                Tags = user.LastTags ?? new List<string>(),
                PreviewUrl = user.LastPreviewUrl,
                StartedAt = DateTime.UtcNow,
                TotalViews = 0
            };

            _context.Streams.Add(stream);
            user.CurrentStream = stream;
            user.IsOnline = true;

            await _context.SaveChangesAsync();

            // 🔔 Уведомления о старте стрима подписчикам
            await _notificationService.NotifyStreamStartedAsync(stream);

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

            await _context.SaveChangesAsync();

            // 🔔 Уведомления о завершении стрима подписчикам
            await _notificationService.NotifyStreamEndedAsync(user.CurrentStream);

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
    }
}
