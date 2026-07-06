using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamChatModerationLogService
    {
        Task LogAsync(
            int streamerId,
            int actorUserId,
            string action,
            int? targetUserId = null,
            string? targetUsername = null,
            string? messageId = null,
            string? details = null);

        Task<(List<StreamChatModerationLogDto> items, int total)> GetLogsAsync(
            int streamerId,
            int page,
            int pageSize);
    }

    public class StreamChatModerationLogService : IStreamChatModerationLogService
    {
        private readonly AppDbContext _context;

        public StreamChatModerationLogService(AppDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            int streamerId,
            int actorUserId,
            string action,
            int? targetUserId = null,
            string? targetUsername = null,
            string? messageId = null,
            string? details = null)
        {
            if (streamerId <= 0 || actorUserId <= 0 || string.IsNullOrWhiteSpace(action))
                return;

            _context.StreamChatModerationLogs.Add(new StreamChatModerationLog
            {
                StreamerId = streamerId,
                ActorUserId = actorUserId,
                Action = action,
                TargetUserId = targetUserId,
                TargetUsername = targetUsername,
                MessageId = messageId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task<(List<StreamChatModerationLogDto> items, int total)> GetLogsAsync(
            int streamerId,
            int page,
            int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, ChatConstants.ModerationLogPageSize);

            var query = _context.StreamChatModerationLogs
                .AsNoTracking()
                .Where(l => l.StreamerId == streamerId);

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new StreamChatModerationLogDto
                {
                    Id = l.Id,
                    ActorUserId = l.ActorUserId,
                    ActorUsername = l.Actor.Nickname,
                    Action = l.Action,
                    TargetUserId = l.TargetUserId,
                    TargetUsername = l.TargetUsername,
                    MessageId = l.MessageId,
                    Details = l.Details,
                    CreatedAt = l.CreatedAt
                })
                .ToListAsync();

            return (items, total);
        }
    }
}
