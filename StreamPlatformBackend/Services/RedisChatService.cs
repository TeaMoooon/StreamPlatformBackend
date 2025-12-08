using StackExchange.Redis;
using StreamPlatformBackend.DTO.StreamDTO;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{
    public interface IRedisChatService
    {
        Task AddMessageAsync(int streamId, ChatMessageDto message);
        Task<List<ChatMessageDto>> GetLastMessagesAsync(int streamId);
        Task SetModeratorsAsync(int streamerId, IEnumerable<int> userIds);
        Task<bool> IsModeratorAsync(int streamerId, int userId);
        Task PublishMessageAsync(int streamId, ChatMessageDto message);
    }

    public class RedisChatService : IRedisChatService
    {
        private readonly IDatabase _db;
        private readonly ISubscriber _subscriber;
        private const int MaxMessages = 50;

        public RedisChatService(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
            _subscriber = redis.GetSubscriber();
        }

        private string GetChatKey(int streamId) => $"chat:{streamId}:messages";
        private string GetModeratorsKey(int streamerId) => $"stream:{streamerId}:moderators";

        public async Task AddMessageAsync(int streamId, ChatMessageDto message)
        {
            var json = JsonSerializer.Serialize(message);
            var key = GetChatKey(streamId);
            await _db.ListRightPushAsync(key, json);
            await _db.ListTrimAsync(key, -MaxMessages, -1);
        }

        public async Task<List<ChatMessageDto>> GetLastMessagesAsync(int streamId)
        {
            var messages = await _db.ListRangeAsync(GetChatKey(streamId), 0, -1);
            return messages.Select(m => JsonSerializer.Deserialize<ChatMessageDto>(m)!).ToList();
        }

        public async Task SetModeratorsAsync(int streamerId, IEnumerable<int> userIds)
        {
            var key = GetModeratorsKey(streamerId);
            await _db.KeyDeleteAsync(key);
            foreach (var id in userIds)
                await _db.SetAddAsync(key, id);
        }

        public async Task<bool> IsModeratorAsync(int streamerId, int userId)
            => await _db.SetContainsAsync(GetModeratorsKey(streamerId), userId);

        public async Task PublishMessageAsync(int streamId, ChatMessageDto message)
        {
            var json = JsonSerializer.Serialize(message);
            await _subscriber.PublishAsync($"chat_channel:{streamId}", json);
        }
    }
}
