using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamChatHistoryService
    {
        Task AddAsync(int streamId, int streamerId, ChatMessageDto message);
        Task MarkDeletedAsync(string messageId, int deletedByUserId);
        Task MarkUserMessagesDeletedAsync(int streamId, int userId, int deletedByUserId);
    }

    public class StreamChatHistoryService : IStreamChatHistoryService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StreamChatHistoryService> _logger;

        public StreamChatHistoryService(AppDbContext context, ILogger<StreamChatHistoryService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task AddAsync(int streamId, int streamerId, ChatMessageDto message)
        {
            if (streamId <= 0 || streamerId <= 0 || message == null || string.IsNullOrWhiteSpace(message.Id))
                return;

            try
            {
                _context.StreamChatMessages.Add(new StreamChatMessage
                {
                    Id = message.Id,
                    StreamId = streamId,
                    StreamerId = streamerId,
                    UserId = message.UserId,
                    Username = Truncate(message.Username, 50),
                    Text = Truncate(message.Text ?? string.Empty, 500),
                    Role = Truncate(string.IsNullOrWhiteSpace(message.Role) ? "User" : message.Role, 32),
                    CreatedAt = message.Timestamp == default ? DateTime.UtcNow : message.Timestamp,
                    OffsetSeconds = message.OffsetSeconds,
                    IsDeleted = message.IsDeleted
                });

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist chat message {MessageId} for stream {StreamId}", message.Id, streamId);
            }
        }

        public async Task MarkDeletedAsync(string messageId, int deletedByUserId)
        {
            if (string.IsNullOrWhiteSpace(messageId) || deletedByUserId <= 0)
                return;

            try
            {
                var message = await _context.StreamChatMessages
                    .FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted);
                if (message == null)
                    return;

                message.IsDeleted = true;
                message.DeletedAt = DateTime.UtcNow;
                message.DeletedByUserId = deletedByUserId;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark chat message {MessageId} deleted", messageId);
            }
        }

        public async Task MarkUserMessagesDeletedAsync(int streamId, int userId, int deletedByUserId)
        {
            if (streamId <= 0 || userId <= 0 || deletedByUserId <= 0)
                return;

            try
            {
                var now = DateTime.UtcNow;
                var messages = await _context.StreamChatMessages
                    .Where(m => m.StreamId == streamId && m.UserId == userId && !m.IsDeleted)
                    .ToListAsync();

                if (messages.Count == 0)
                    return;

                foreach (var message in messages)
                {
                    message.IsDeleted = true;
                    message.DeletedAt = now;
                    message.DeletedByUserId = deletedByUserId;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark chat messages deleted for user {UserId} in stream {StreamId}", userId, streamId);
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            value ??= string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
