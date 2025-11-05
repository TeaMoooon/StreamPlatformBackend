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
    }

    // Services/StreamService.cs
    public class StreamService : IStreamService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StreamService> _logger;

        public StreamService(AppDbContext context, ILogger<StreamService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<StreamModel> StartStreamAsync(int userId, string streamKey)
        {
            try
            {
                _logger.LogInformation("Starting stream for user {UserId} with key {StreamKey}", userId, streamKey);

                var user = await _context.Users
                    .Include(u => u.CurrentStream)
                    //.ThenInclude(s => s.Category)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    throw new ArgumentException($"User with ID {userId} not found");

                if (user.StreamKey != streamKey)
                    throw new UnauthorizedAccessException("Invalid stream key");

                // Если стрим уже активен, возвращаем его
                if (user.CurrentStream != null && user.CurrentStream.EndedAt == null)
                {
                    _logger.LogWarning("Stream already active for user {UserId}", userId);
                    return user.CurrentStream;
                }

                // Создаем новый стрим
                var stream = new StreamModel
                {
                    UserId = user.Id,
                    StreamName = user.LastStreamName ?? $"{user.Nickname}'s Stream",
                    //CategoryId = user.LastCategoryId ?? await GetDefaultCategoryIdAsync(),
                    Tags = user.LastTags ?? Array.Empty<string>(),
                    PreviewlUrl = user.LastPreviewlUrl,
                    StartedAt = DateTime.UtcNow,
                    EndedAt = null,
                    TotalViews = 0
                };

                _context.Streams.Add(stream);
                user.CurrentStream = stream;
                user.IsOnline = true;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Stream started successfully for user {UserId}. Stream ID: {StreamId}",
                    userId, stream.Id);

                return stream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting stream for user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> EndStreamAsync(int userId, string streamKey)
        {
            try
            {
                _logger.LogInformation("Ending stream for user {UserId}", userId);

                var user = await _context.Users
                    .Include(u => u.CurrentStream)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user?.CurrentStream == null || user.StreamKey != streamKey)
                {
                    _logger.LogWarning("No active stream found for user {UserId} or invalid stream key", userId);
                    return false;
                }

                user.CurrentStream.EndedAt = DateTime.UtcNow;
                user.IsOnline = false;

                // Сохраняем последние настройки для будущих стримов
                user.LastStreamName = user.CurrentStream.StreamName;
                //user.LastCategoryId = user.CurrentStream.CategoryId;
                user.LastTags = user.CurrentStream.Tags;
                user.LastPreviewlUrl = user.CurrentStream.PreviewlUrl;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Stream ended successfully for user {UserId}. Stream duration: {Duration}",
                    userId, DateTime.UtcNow - user.CurrentStream.StartedAt);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending stream for user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> UpdateStreamAsync(int userId, StreamUpdateDto updateDto)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.CurrentStream)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user?.CurrentStream == null)
                    return false;

                var stream = user.CurrentStream;

                if (!string.IsNullOrEmpty(updateDto.StreamName))
                    stream.StreamName = updateDto.StreamName;
                /*
                if (updateDto.CategoryId.HasValue)
                {
                    var categoryExists = await _context.StreamCategories
                        .AnyAsync(c => c.Id == updateDto.CategoryId.Value);
                    if (categoryExists)
                        stream.CategoryId = updateDto.CategoryId.Value;
                }*/

                if (updateDto.Tags != null)
                    stream.Tags = updateDto.Tags;

                if (updateDto.PreviewlUrl != null)
                    stream.PreviewlUrl = updateDto.PreviewlUrl;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating stream for user {UserId}", userId);
                return false;
            }
        }

        public async Task<StreamInfoDto?> GetStreamInfoAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.CurrentStream == null)
                return null;

            return MapToStreamInfoDto(user.CurrentStream, user);
        }

        public async Task<bool> ValidateStreamKeyAsync(string streamKey)
        {
            if (!TryParseUserIdFromStreamKey(streamKey, out int userId))
                return false;

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.StreamKey == streamKey);

            return user != null;
        }

        public async Task<bool> IsUserStreamingAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId);

            return user?.IsOnline == true && user.CurrentStream?.EndedAt == null;
        }

        public async Task<int> IncrementViewCountAsync(int streamId)
        {
            var stream = await _context.Streams.FindAsync(streamId);
            if (stream == null)
                return 0;

            stream.TotalViews++;
            await _context.SaveChangesAsync();

            return stream.TotalViews;
        }

        // Вспомогательные методы
        /*
        private async Task<int> GetDefaultCategoryIdAsync()
        {
            var defaultCategory = await _context.StreamCategories
                .FirstOrDefaultAsync(c => c.Name == "Just Chatting");

            return defaultCategory?.Id ?? 1; // Fallback to ID 1
        }/*/

        private bool TryParseUserIdFromStreamKey(string streamKey, out int userId)
        {
            userId = 0;
            if (string.IsNullOrEmpty(streamKey) || !streamKey.StartsWith("live_"))
                return false;

            var parts = streamKey.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out userId))
                return true;

            return false;
        }

        private StreamInfoDto MapToStreamInfoDto(StreamModel stream, UserModel user)
        {
            return new StreamInfoDto
            {
                StreamId = stream.Id,
                StreamName = stream.StreamName,
                StreamerName = user.Nickname,
                StreamerId = user.Id,
                //Category = stream.Category?.Name ?? "Unknown",
                Tags = stream.Tags,
                PreviewlUrl = stream.PreviewlUrl,
                HlsUrl = $"/hls/{user.StreamKey}.m3u8",
                TotalViews = stream.TotalViews,
                StartedAt = stream.StartedAt,
                IsLive = stream.EndedAt == null
            };
        }
    }
}
