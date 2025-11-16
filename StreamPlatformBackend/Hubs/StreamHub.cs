using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    /// <summary>
    /// SignalR хаб для работы со стримами:
    /// - Подключение к стриму
    /// - Подсчет уникальных зрителей
    /// - Уведомления подписчиков
    /// - Обновление статуса стрима в реальном времени
    /// </summary>
    public class StreamHub : Hub
    {
        private readonly IStreamService _streamService;
        private readonly IUserService _userService;
        private readonly ILogger<StreamHub> _logger;

        /// <summary>Словарь уникальных зрителей: ключ = streamerId, значение = HashSet viewerKey (UserId или sessionId)</summary>
        private static readonly Dictionary<int, HashSet<string>> StreamViewers = new();

        /// <summary>Маппинг ConnectionId → (StreamerId, ViewerKey)</summary>
        private static readonly Dictionary<string, (int StreamerId, string ViewerKey)> ConnectionMap = new();

        public StreamHub(IStreamService streamService, IUserService userService, ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _logger = logger;
        }

        /// <summary>
        /// Пользователь присоединяется к стриму
        /// </summary>
        /// <param name="streamerUsername">Ник стримера</param>
        /// <param name="sessionId">Идентификатор сессии для гостей</param>
        public async Task JoinStream(string streamerUsername, string sessionId = null)
        {
            try
            {
                var streamer = await _userService.GetUserByNameAsync(streamerUsername);
                if (streamer == null)
                {
                    await Clients.Caller.SendAsync("Error", "Streamer not found");
                    return;
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");

                string viewerKey;
                var userId = GetCurrentUserId();
                if (userId > 0)
                    viewerKey = $"user_{userId}";
                else
                    viewerKey = sessionId ?? Context.ConnectionId;

                // Потокобезопасное добавление
                lock (StreamViewers)
                {
                    if (!StreamViewers.ContainsKey(streamer.Id))
                        StreamViewers[streamer.Id] = new HashSet<string>();
                    StreamViewers[streamer.Id].Add(viewerKey);

                    ConnectionMap[Context.ConnectionId] = (streamer.Id, viewerKey);
                }

                // Отправляем обновлённое количество зрителей
                var count = StreamViewers[streamer.Id].Count;
                await Clients.Group($"stream_{streamer.Id}").SendAsync("UpdateViewerCount", count);

                // Получаем информацию о стриме
                var streamInfo = await _streamService.GetStreamInfoAsync(streamer.Id);
                if (streamInfo != null)
                {
                    await Clients.Caller.SendAsync("StreamJoined", streamInfo);
                }
                else
                {
                    // Если стрим ещё не начался, отправляем заглушку
                    await Clients.Caller.SendAsync("StreamJoined", new
                    {
                        IsLive = false,
                        StreamerId = streamer.Id,
                        StreamerName = streamer.Nickname
                    });
                }

                _logger.LogInformation("Viewer {ViewerKey} joined stream {StreamerId}", viewerKey, streamer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JoinStream for streamer {StreamerUsername}", streamerUsername);
                await Clients.Caller.SendAsync("Error", "Failed to join stream: " + ex.Message);
            }
        }

        /// <summary>
        /// Пользователь покидает стрим
        /// </summary>
        /// <param name="streamerUsername">Ник стримера</param>
        /// <param name="sessionId">Идентификатор сессии для гостей</param>
        public async Task LeaveStream(string streamerUsername, string sessionId = null)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer != null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");

                string viewerKey;
                var userId = GetCurrentUserId();
                if (userId > 0)
                    viewerKey = $"user_{userId}";
                else
                    viewerKey = sessionId ?? Context.ConnectionId;

                lock (StreamViewers)
                {
                    if (StreamViewers.ContainsKey(streamer.Id))
                        StreamViewers[streamer.Id].Remove(viewerKey);

                    ConnectionMap.Remove(Context.ConnectionId);
                }

                var count = StreamViewers.ContainsKey(streamer.Id) ? StreamViewers[streamer.Id].Count : 0;
                await Clients.Group($"stream_{streamer.Id}").SendAsync("UpdateViewerCount", count);

                _logger.LogInformation("Viewer {ViewerKey} left stream {StreamerId}", viewerKey, streamer.Id);
            }
        }

        /// <summary>
        /// Подписка на уведомления о стримах (для подписчиков)
        /// </summary>
        [Authorize]
        public async Task SubscribeToMySubscriptions()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);

            foreach (var streamerId in subscriptions)
                await Groups.AddToGroupAsync(Context.ConnectionId, $"notifications_{streamerId}");

            _logger.LogInformation("User {UserId} subscribed to notifications for {Count} streamers", userId, subscriptions.Count);
        }

        /// <summary>
        /// Отписка от уведомлений стримера
        /// </summary>
        /// <param name="streamerId">ID стримера</param>
        [Authorize]
        public async Task UnsubscribeFromStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"notifications_{streamerId}");

            _logger.LogInformation("User {UserId} unsubscribed from notifications of streamer {StreamerId}", userId, streamerId);
        }

        /// <summary>
        /// Обновление статуса стрима (IsLive) для всех зрителей
        /// </summary>
        /// <param name="streamerId">ID стримера</param>
        public async Task UpdateStreamStatus(int streamerId)
        {
            var streamInfo = await _streamService.GetStreamInfoAsync(streamerId);
            await Clients.Group($"stream_{streamerId}").SendAsync("StreamStatusUpdated", streamInfo);
        }

        /// <summary>
        /// Обработка отключения пользователя: удаляем из счетчика зрителей
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            (int StreamerId, string ViewerKey) info;

            lock (StreamViewers)
            {
                // Проверяем, есть ли запись для этого подключения
                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out info))
                {
                    return; // пользователь не был в стриме
                }

                // Удаляем соединение из карты
                ConnectionMap.Remove(Context.ConnectionId);

                // Удаляем зрителя из списка уникальных
                if (StreamViewers.TryGetValue(info.StreamerId, out var viewers))
                {
                    viewers.Remove(info.ViewerKey);

                    // Отправляем обновлённый счётчик
                    _ = Clients.Group($"stream_{info.StreamerId}")
                        .SendAsync("UpdateViewerCount", viewers.Count);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Получаем текущий ID пользователя из Claims (0 для гостей)
        /// </summary>
        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }
    }
}
