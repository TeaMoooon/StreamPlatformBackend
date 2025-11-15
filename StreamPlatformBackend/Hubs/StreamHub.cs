using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    /// <summary>
    /// SignalR хаб для работы со стримами:
    /// - Подключение к стриму
    /// - Уведомления о начале/конце стрима для подписчиков
    /// </summary>
    public class StreamHub : Hub
    {
        private readonly IStreamService _streamService;
        private readonly IUserService _userService;
        private readonly ILogger<StreamHub> _logger;

        public StreamHub(IStreamService streamService, IUserService userService, ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Пользователь присоединяется к конкретному стриму (любой, авторизация не нужна)
        /// </summary>
        public async Task JoinStream(string streamerUsername)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer == null)
            {
                await Clients.Caller.SendAsync("Error", "Streamer not found");
                return;
            }

            // Подключаем к группе стрима
            await Groups.AddToGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");

            var streamInfo = await _streamService.GetStreamInfoAsync(streamer.Id);
            await Clients.Caller.SendAsync("StreamJoined", streamInfo);

            _logger.LogInformation("User {ConnectionId} joined stream {StreamerId}", Context.ConnectionId, streamer.Id);
        }

        /// <summary>
        /// Пользователь покидает стрим
        /// </summary>
        public async Task LeaveStream(string streamerUsername)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer != null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");
                _logger.LogInformation("User {ConnectionId} left stream {StreamerId}", Context.ConnectionId, streamer.Id);
            }
        }

        /// <summary>
        /// Подписка на уведомления о стримах от подписанных стримеров
        /// Авторизация обязательна
        /// </summary>
        [Authorize]
        public async Task SubscribeToMySubscriptions()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            // Получаем список стримеров, на которых подписан пользователь
            var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);

            foreach (var streamerId in subscriptions)
            {
                // Добавляем текущее подключение в группу каждого стримера
                await Groups.AddToGroupAsync(Context.ConnectionId, $"notifications_{streamerId}");
            }

            _logger.LogInformation("User {UserId} subscribed to notifications for {Count} streamers", userId, subscriptions.Count);
        }

        /// <summary>
        /// Отписка от уведомлений
        /// </summary>
        [Authorize]
        public async Task UnsubscribeFromStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            // Удаляем подключение из группы стримера
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"notifications_{streamerId}");

            _logger.LogInformation("User {UserId} unsubscribed from notifications of streamer {StreamerId}", userId, streamerId);
        }

        /// <summary>
        /// При подключении помечаем пользователя онлайн (если авторизован)
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                await _userService.UpdateUserOnlineStatusAsync(userId, true);
                _logger.LogInformation("User {UserId} connected to StreamHub", userId);
            }

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// При отключении помечаем пользователя оффлайн
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                await _userService.UpdateUserOnlineStatusAsync(userId, false);
                _logger.LogInformation("User {UserId} disconnected from StreamHub", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Получаем текущий ID пользователя из Claims
        /// </summary>
        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }
    }
}
