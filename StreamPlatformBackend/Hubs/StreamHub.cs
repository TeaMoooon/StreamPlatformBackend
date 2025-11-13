using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Services;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    public class StreamHub : Hub
    {
        private readonly IStreamService _streamService;
        private readonly IUserService _userService;
        private readonly ILogger<StreamHub> _logger;
        private static readonly ConcurrentDictionary<int, string> _userConnections = new();

        public StreamHub(IStreamService streamService, IUserService userService, ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _logger = logger;
        }

        // Подключение к хабу конкретного стрима
        public async Task JoinStream(string streamerUsername)
        {
            try
            {
                var streamer = await _userService.GetUserByNameAsync(streamerUsername);
                if (streamer == null)
                {
                    await Clients.Caller.SendAsync("Error", "Streamer not found");
                    return;
                }

                // Добавляем connection в группу стрима
                await Groups.AddToGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");

                // Получаем информацию о стриме
                var streamInfo = await _streamService.GetStreamInfoAsync(streamer.Id);

                await Clients.Caller.SendAsync("StreamJoined", streamInfo);

                _logger.LogInformation("User {ConnectionId} joined stream {StreamerId}", Context.ConnectionId, streamer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining stream {StreamerUsername}", streamerUsername);
                await Clients.Caller.SendAsync("Error", "Failed to join stream");
            }
        }

        // Покидание стрима
        public async Task LeaveStream(string streamerUsername)
        {
            try
            {
                var streamer = await _userService.GetUserByNameAsync(streamerUsername);
                if (streamer != null)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");
                    _logger.LogInformation("User {ConnectionId} left stream {StreamerId}", Context.ConnectionId, streamer.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving stream {StreamerUsername}", streamerUsername);
            }
        }

        // Подписка на уведомления о начале стримов от подписанных стримеров
        public async Task SubscribeToStreamNotifications()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId > 0)
                {
                    // Добавляем connection в группу уведомлений пользователя
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"notifications_{userId}");

                    // Сохраняем связь пользователя с connection
                    _userConnections[userId] = Context.ConnectionId;

                    _logger.LogInformation("User {UserId} subscribed to stream notifications", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to stream notifications");
            }
        }

        // Вызывается когда пользователь соединяется
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                _userConnections.AddOrUpdate(userId, Context.ConnectionId, (key, oldValue) => Context.ConnectionId);
                await _userService.UpdateUserOnlineStatusAsync(userId, true);
                _logger.LogInformation("User {UserId} connected to StreamHub", userId);
            }

            await base.OnConnectedAsync();
        }

        // Вызывается когда пользователь отсоединяется
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                _userConnections.TryRemove(userId, out _);
                await _userService.UpdateUserOnlineStatusAsync(userId, false);
                _logger.LogInformation("User {UserId} disconnected from StreamHub", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }
            return 0;
        }
    }
}