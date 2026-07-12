using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Hubs;

namespace StreamPlatformBackend.Services
{
    public interface IChatNicknameNotifier
    {
        Task NotifyNicknameChangedAsync(int userId, string newNickname);
    }

    public class ChatNicknameNotifier : IChatNicknameNotifier
    {
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly IRedisChatService _redisChatService;
        private readonly ILogger<ChatNicknameNotifier> _logger;

        public ChatNicknameNotifier(
            IHubContext<StreamHub> hubContext,
            IRedisChatService redisChatService,
            ILogger<ChatNicknameNotifier> logger)
        {
            _hubContext = hubContext;
            _redisChatService = redisChatService;
            _logger = logger;
        }

        public async Task NotifyNicknameChangedAsync(int userId, string newNickname)
        {
            try
            {
                await _redisChatService.UpdateUsernameForUserAsync(userId, newNickname);
                await _hubContext.Clients.All.SendAsync("ChatUserNicknameChanged", new
                {
                    userId,
                    username = newNickname
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify chat nickname change for user {UserId}", userId);
            }
        }
    }
}
