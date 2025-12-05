using Microsoft.EntityFrameworkCore.Storage;
using StackExchange.Redis;
using StreamPlatformBackend.DTO.StreamDTO;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{

    public interface IRedisChatService
    {
        Task AddMessageAsync(int streamerId, ChatMessageDto message);

        Task<List<ChatMessageDto>> GetLastMessagesAsync(int streamerId);

        Task SetModeratorsAsync(int streamerId, IEnumerable<int> userIds);

        Task<bool> IsModeratorAsync(int streamerId, int userId);

        Task PublishMessageAsync(int streamerId, ChatMessageDto message);
    }
    public class RedisChatService : IRedisChatService
    {
        private readonly StackExchange.Redis.IDatabase _db;
        private readonly StackExchange.Redis.ISubscriber _subscriber;
        private const int MaxMessages = 50; // последние 50 сообщений

        public RedisChatService(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
            _subscriber = redis.GetSubscriber();
        }

        private string GetChatKey(int streamerId) => $"chat:{streamerId}:messages";
        private string GetModeratorsKey(int streamerId) => $"stream:{streamerId}:moderators";

        // Сохраняем сообщение в Redis List
        public async Task AddMessageAsync(int streamerId, ChatMessageDto message)
        {
            var json = JsonSerializer.Serialize(message);
            var key = GetChatKey(streamerId);
            await _db.ListRightPushAsync(key, json);
            await _db.ListTrimAsync(key, -MaxMessages, -1); // держим только последние MaxMessages
        }

        // Получаем последние сообщения
        public async Task<List<ChatMessageDto>> GetLastMessagesAsync(int streamerId)
        {
            var key = GetChatKey(streamerId);
            var messages = await _db.ListRangeAsync(key, 0, -1);
            return messages.Select(m => JsonSerializer.Deserialize<ChatMessageDto>(m)!)
                           .ToList();
        }

        // Модераторы
        public async Task SetModeratorsAsync(int streamerId, IEnumerable<int> userIds)
        {
            var key = GetModeratorsKey(streamerId);
            await _db.KeyDeleteAsync(key);
            foreach (var id in userIds)
                await _db.SetAddAsync(key, id);
        }

        public async Task<bool> IsModeratorAsync(int streamerId, int userId)
        {
            var key = GetModeratorsKey(streamerId);
            return await _db.SetContainsAsync(key, userId);
        }

        // Pub/Sub если нужно
        public async Task PublishMessageAsync(int streamerId, ChatMessageDto message)
        {
            var channel = $"chat_channel:{streamerId}";
            var json = JsonSerializer.Serialize(message);
            await _subscriber.PublishAsync(channel, json);
        }
    }
}

