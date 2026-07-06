using StackExchange.Redis;
using StreamPlatformBackend.Constants;
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
        Task SetAssistantsAsync(int streamerId, IEnumerable<int> userIds);
        Task<bool> IsAssistantAsync(int streamerId, int userId);
        Task<bool> IsChatManagerAsync(int streamerId, int userId);
        Task PublishMessageAsync(int streamId, ChatMessageDto message);
        Task<int> GetSlowModeSecondsAsync(int streamerId);
        Task SetSlowModeSecondsAsync(int streamerId, int seconds);
        Task<string> GetChatRulesAsync(int streamerId);
        Task SetChatRulesAsync(int streamerId, string rules);
        Task<int?> CheckSlowModeAsync(int streamerId, int userId, bool bypassSlowMode);
        Task RegisterMessageSentAsync(int streamerId, int userId, int slowModeSeconds);
        Task<bool> MarkMessageDeletedAsync(int streamId, string messageId);
        Task MarkUserMessagesDeletedAsync(int streamId, int userId);
        Task SetTimeoutAsync(int streamerId, int userId, int seconds);
        Task<int?> GetTimeoutRemainingAsync(int streamerId, int userId);
        Task SetBanAsync(int streamerId, int userId, bool banned);
        Task SetBansAsync(int streamerId, IEnumerable<int> userIds);
        Task<bool> IsBannedAsync(int streamerId, int userId);
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
        private string GetAssistantsKey(int streamerId) => $"stream:{streamerId}:assistants";
        private string GetSlowModeKey(int streamerId) => $"chat:{streamerId}:settings:slowmode";
        private string GetChatRulesKey(int streamerId) => $"chat:{streamerId}:settings:rules";
        private string GetLastMessageKey(int streamerId, int userId) => $"chat:{streamerId}:lastmsg:{userId}";
        private string GetTimeoutKey(int streamerId, int userId) => $"chat:{streamerId}:timeout:{userId}";
        private string GetBansKey(int streamerId) => $"chat:{streamerId}:bans";

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

        public async Task SetAssistantsAsync(int streamerId, IEnumerable<int> userIds)
        {
            var key = GetAssistantsKey(streamerId);
            await _db.KeyDeleteAsync(key);
            foreach (var id in userIds)
                await _db.SetAddAsync(key, id);
        }

        public async Task<bool> IsAssistantAsync(int streamerId, int userId)
            => await _db.SetContainsAsync(GetAssistantsKey(streamerId), userId);

        public async Task<bool> IsChatManagerAsync(int streamerId, int userId)
        {
            if (await IsModeratorAsync(streamerId, userId))
                return true;
            return await IsAssistantAsync(streamerId, userId);
        }

        public async Task PublishMessageAsync(int streamId, ChatMessageDto message)
        {
            var json = JsonSerializer.Serialize(message);
            await _subscriber.PublishAsync($"chat_channel:{streamId}", json);
        }

        public async Task<int> GetSlowModeSecondsAsync(int streamerId)
        {
            var value = await _db.StringGetAsync(GetSlowModeKey(streamerId));
            if (!value.HasValue || !int.TryParse(value, out var seconds))
                return 0;
            return Math.Clamp(seconds, 0, ChatConstants.MaxSlowModeSeconds);
        }

        public async Task SetSlowModeSecondsAsync(int streamerId, int seconds)
        {
            seconds = Math.Clamp(seconds, 0, ChatConstants.MaxSlowModeSeconds);
            await _db.StringSetAsync(GetSlowModeKey(streamerId), seconds);
        }

        public async Task<string> GetChatRulesAsync(int streamerId)
        {
            var value = await _db.StringGetAsync(GetChatRulesKey(streamerId));
            return value.HasValue ? value.ToString() : string.Empty;
        }

        public async Task SetChatRulesAsync(int streamerId, string rules)
        {
            rules = (rules ?? string.Empty).Trim();
            if (rules.Length > ChatConstants.MaxChatRulesLength)
                rules = rules[..ChatConstants.MaxChatRulesLength];

            if (string.IsNullOrEmpty(rules))
                await _db.KeyDeleteAsync(GetChatRulesKey(streamerId));
            else
                await _db.StringSetAsync(GetChatRulesKey(streamerId), rules);
        }

        public async Task<int?> CheckSlowModeAsync(int streamerId, int userId, bool bypassSlowMode)
        {
            if (bypassSlowMode)
                return null;

            var slowModeSeconds = await GetSlowModeSecondsAsync(streamerId);
            if (slowModeSeconds <= 0)
                return null;

            var lastValue = await _db.StringGetAsync(GetLastMessageKey(streamerId, userId));
            if (!lastValue.HasValue || !long.TryParse(lastValue, out var lastUnix))
                return null;

            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastUnix;
            if (elapsed >= slowModeSeconds)
                return null;

            return (int)Math.Ceiling((double)(slowModeSeconds - elapsed));
        }

        public async Task RegisterMessageSentAsync(int streamerId, int userId, int slowModeSeconds)
        {
            if (slowModeSeconds <= 0)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _db.StringSetAsync(
                GetLastMessageKey(streamerId, userId),
                now,
                TimeSpan.FromSeconds(slowModeSeconds + 10));
        }

        public async Task<bool> MarkMessageDeletedAsync(int streamId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
                return false;

            var key = GetChatKey(streamId);
            var messages = await _db.ListRangeAsync(key, 0, -1);

            for (var i = 0; i < messages.Length; i++)
            {
                var message = JsonSerializer.Deserialize<ChatMessageDto>(messages[i]!);
                if (message?.Id != messageId || message.IsDeleted)
                    continue;

                message.IsDeleted = true;
                await _db.ListSetByIndexAsync(key, i, JsonSerializer.Serialize(message));
                return true;
            }

            return false;
        }

        public async Task MarkUserMessagesDeletedAsync(int streamId, int userId)
        {
            var key = GetChatKey(streamId);
            var messages = await _db.ListRangeAsync(key, 0, -1);

            for (var i = 0; i < messages.Length; i++)
            {
                var message = JsonSerializer.Deserialize<ChatMessageDto>(messages[i]!);
                if (message == null || message.IsDeleted || message.UserId != userId)
                    continue;

                message.IsDeleted = true;
                await _db.ListSetByIndexAsync(key, i, JsonSerializer.Serialize(message));
            }
        }

        public async Task SetTimeoutAsync(int streamerId, int userId, int seconds)
        {
            var key = GetTimeoutKey(streamerId, userId);
            seconds = Math.Clamp(seconds, 0, ChatConstants.MaxTimeoutSeconds);

            if (seconds <= 0)
            {
                await _db.KeyDeleteAsync(key);
                return;
            }

            var until = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + seconds;
            await _db.StringSetAsync(key, until, TimeSpan.FromSeconds(seconds + 30));
        }

        public async Task<int?> GetTimeoutRemainingAsync(int streamerId, int userId)
        {
            var value = await _db.StringGetAsync(GetTimeoutKey(streamerId, userId));
            if (!value.HasValue || !long.TryParse(value, out var untilUnix))
                return null;

            var remaining = untilUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (remaining <= 0)
                return null;

            return (int)Math.Ceiling((double)remaining);
        }

        public async Task SetBanAsync(int streamerId, int userId, bool banned)
        {
            var key = GetBansKey(streamerId);
            if (banned)
                await _db.SetAddAsync(key, userId);
            else
                await _db.SetRemoveAsync(key, userId);
        }

        public async Task SetBansAsync(int streamerId, IEnumerable<int> userIds)
        {
            var key = GetBansKey(streamerId);
            await _db.KeyDeleteAsync(key);
            foreach (var userId in userIds)
                await _db.SetAddAsync(key, userId);
        }

        public async Task<bool> IsBannedAsync(int streamerId, int userId)
            => await _db.SetContainsAsync(GetBansKey(streamerId), userId);
    }
}
