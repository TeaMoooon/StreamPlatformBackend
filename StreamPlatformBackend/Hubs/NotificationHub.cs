using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    /// <summary>
    /// Receive-only hub for personal and subscription notification groups.
    /// Outbound delivery is server-side only via <see cref="Services.NotificationService.INotificationSender"/>
    /// and <c>IHubContext&lt;NotificationHub&gt;</c> — clients must never be able to broadcast.
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
        /// On connect: join personal group + groups for streamers the user actually follows.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0)
            {
                await base.OnConnectedAsync();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

            _logger.LogInformation("User {UserId} connected to NotificationHub", userId);

            var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);
            foreach (var streamerId in subscriptions)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, StreamerSubsGroup(streamerId));
            }

            _logger.LogInformation(
                "User {UserId} subscribed to {Count} streamer groups",
                userId, subscriptions.Count
            );

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Join a streamer's subscriber group only if the caller actively follows them.
        /// </summary>
        public async Task SubscribeToStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0 || streamerId <= 0)
                return;

            if (!await _userService.IsSubscribedAsync(userId, streamerId))
            {
                _logger.LogWarning(
                    "User {UserId} attempted SubscribeToStreamer({StreamerId}) without an active follow",
                    userId, streamerId);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, StreamerSubsGroup(streamerId));

            _logger.LogInformation("User {UserId} subscribed to streamer {StreamerId}",
                userId, streamerId);
        }

        /// <summary>
        /// Leave a streamer's subscriber group (safe even if not a member).
        /// </summary>
        public async Task UnsubscribeFromStreamer(int streamerId)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0 || streamerId <= 0)
                return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, StreamerSubsGroup(streamerId));

            _logger.LogInformation("User {UserId} unsubscribed from streamer {StreamerId}",
                userId, streamerId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetCurrentUserId();
            if (userId > 0)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId));

                var subscriptions = await _userService.GetSubscribedStreamerIdsAsync(userId);
                foreach (var streamerId in subscriptions)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, StreamerSubsGroup(streamerId));
                }

                _logger.LogInformation("User {UserId} disconnected from NotificationHub", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        public static string UserGroup(int userId) => $"user_{userId}";
        public static string StreamerSubsGroup(int streamerId) => $"streamer_subs_{streamerId}";
    }
}
