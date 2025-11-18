using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    /// <summary>
    /// Хаб для личных и групповых уведомлений.
    /// Используется для:
    /// - Уведомлений о новом стриме
    /// - Уведомлений о подписках
    /// - Личных уведомлений
    /// - Системных событий
    /// </summary>
    [Authorize]
    public class NotificationHub : Hub
    {
        private readonly IUserService _userService;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(IUserService userService, ILogger<NotificationHub> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Пользователь подключается, и мы автоматически подписываем его на:
        /// - Личный канал уведомлений user_{userId}
        /// - Все каналы стримеров, на которых он подписан
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0)
            {
                await base.OnConnectedAsync();
                return;
            }

            // Личная группа для уведомлений
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

            _logger.LogInformation("User {UserId} connected to NotificationHub", userId);

            // Подписываем на всех стримеров, которых он фоловит
            var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);
            foreach (var streamerId in subscriptions)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"streamer_subs_{streamerId}");
            }

            _logger.LogInformation(
                "User {UserId} subscribed to {Count} streamer groups",
                userId, subscriptions.Count
            );

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Ручная подписка на уведомления стримера
        /// </summary>
        public async Task SubscribeToStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, $"streamer_subs_{streamerId}");

            _logger.LogInformation("User {UserId} subscribed to streamer {StreamerId}",
                userId, streamerId);
        }

        /// <summary>
        /// Ручная отписка от уведомлений стримера
        /// </summary>
        public async Task UnsubscribeFromStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"streamer_subs_{streamerId}");

            _logger.LogInformation("User {UserId} unsubscribed from streamer {StreamerId}",
                userId, streamerId);
        }

        /// <summary>
        /// Отправить личное уведомление пользователю
        /// </summary>
        public async Task SendPersonalNotification(int targetUserId, string message, string type = "info")
        {
            await Clients.Group($"user_{targetUserId}").SendAsync("ReceiveNotification", new
            {
                Type = type,
                Message = message,
                Date = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Уведомить всех подписчиков стримера (например: стрим начался)
        /// </summary>
        public async Task NotifyStreamerSubscribers(int streamerId, string message)
        {
            await Clients.Group($"streamer_subs_{streamerId}").SendAsync("ReceiveNotification", new
            {
                Type = "stream",
                Message = message,
                StreamerId = streamerId,
                Date = DateTime.UtcNow
            });

            _logger.LogInformation("Sent notification to stream subscribers of {StreamerId}", streamerId);
        }

        /// <summary>
        /// Получить ID текущего пользователя
        /// </summary>
        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }




        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");

                var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);
                foreach (var streamerId in subscriptions)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"streamer_subs_{streamerId}");
                }

                _logger.LogInformation("User {UserId} disconnected from NotificationHub", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
