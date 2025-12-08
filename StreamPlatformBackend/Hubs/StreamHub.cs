using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    public class StreamHub : Hub
    {
        private readonly IStreamService _streamService;
        private readonly IUserService _userService;
        private readonly IRedisChatService _redisChatService;
        private readonly ILogger<StreamHub> _logger;

        // Потокобезопасные коллекции
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> StreamViewers = new();
        private static readonly ConcurrentDictionary<string, (int StreamerId, string ViewerKey, int StreamId)> ConnectionMap = new();

        public StreamHub(
            IStreamService streamService,
            IUserService userService,
            IRedisChatService redisChatService,
            ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _redisChatService = redisChatService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        private string GetViewerKey(int userId, string? sessionId)
            => userId > 0 ? $"user_{userId}" : sessionId ?? Context.ConnectionId;

        public async Task JoinStream(string streamerUsername, string? sessionId = null)
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

                int userId = GetCurrentUserId();
                string viewerKey = GetViewerKey(userId, sessionId);

                var streamInfo = await _streamService.GetStreamInfoAsync(streamer.Id);
                int streamId = streamInfo?.StreamId ?? -1;

                var viewers = StreamViewers.GetOrAdd(streamer.Id, _ => new ConcurrentDictionary<string, byte>());
                viewers[viewerKey] = 0;

                ConnectionMap[Context.ConnectionId] = (streamer.Id, viewerKey, streamId);

                await Clients.Group($"stream_{streamer.Id}")
                    .SendAsync("UpdateViewerCount", viewers.Count);

                await Clients.Caller.SendAsync("StreamJoined", streamInfo ?? new StreamInfoDto
                {
                    IsLive = false,
                    StreamerId = streamer.Id,
                    StreamerName = streamer.Nickname
                });


                await LoadChatHistory();

                _logger.LogInformation("Viewer {ViewerKey} joined stream {StreamerId}", viewerKey, streamer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JoinStream for streamer {StreamerUsername}", streamerUsername);
                await Clients.Caller.SendAsync("Error", "Failed to join stream: " + ex.Message);
            }
        }

        public async Task LeaveStream(string streamerUsername, string? sessionId = null)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer == null) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"stream_{streamer.Id}");

            int userId = GetCurrentUserId();
            string viewerKey = GetViewerKey(userId, sessionId);

            if (StreamViewers.TryGetValue(streamer.Id, out var viewers))
            {
                viewers.TryRemove(viewerKey, out _);
            }

            ConnectionMap.TryRemove(Context.ConnectionId, out _);

            await Clients.Group($"stream_{streamer.Id}")
                .SendAsync("UpdateViewerCount", viewers?.Count ?? 0);

            _logger.LogInformation("Viewer {ViewerKey} left stream {StreamerId}", viewerKey, streamer.Id);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (!ConnectionMap.TryRemove(Context.ConnectionId, out var info))
            {
                await base.OnDisconnectedAsync(exception);
                return;
            }

            if (StreamViewers.TryGetValue(info.StreamerId, out var viewers))
            {
                viewers.TryRemove(info.ViewerKey, out _);
                await Clients.Group($"stream_{info.StreamerId}")
                    .SendAsync("UpdateViewerCount", viewers.Count);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task UpdateStreamStatus(int streamerId)
        {
            var streamInfo = await _streamService.GetStreamInfoAsync(streamerId);

            await Clients.Group($"stream_{streamerId}")
                .SendAsync("StreamStatusChanged", new
                {
                    Status = streamInfo?.IsLive == true ? "Live" : "Offline",
                    Stream = streamInfo
                });
        }

        public async Task SendChatMessage(string text)
        {
            if (!Context.User.Identity.IsAuthenticated)
            {
                await Clients.Caller.SendAsync("Error", "Unauthorized");
                return;
            }

            int userId = GetCurrentUserId();
            string username = Context.User.Identity.Name ?? "Unknown";

            if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
            {
                await Clients.Caller.SendAsync("Error", "NotJoinedToStream");
                return;
            }

            int streamerId = info.StreamerId;

            string role = "User";
            if (userId == streamerId) role = "Streamer";
            else if (await _redisChatService.IsModeratorAsync(streamerId, userId)) role = "Moderator";
            else if (Context.User.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin"))
                role = "Admin";

            var streamInfo = await _streamService.GetStreamInfoAsync(streamerId);
            double offset = streamInfo?.StartedAt != null
                ? (DateTime.UtcNow - streamInfo.StartedAt.Value).TotalSeconds
                : 0;

            var message = new ChatMessageDto
            {
                UserId = userId,
                Username = username,
                Text = text,
                Role = role,
                Timestamp = DateTime.UtcNow,
                OffsetSeconds = offset
            };

            await _redisChatService.AddMessageAsync(info.StreamId, message);
            await _redisChatService.PublishMessageAsync(info.StreamId, message);

            await Clients.Group($"stream_{streamerId}")
                .SendAsync("ReceiveChatMessage", message);
        }

        public async Task LoadChatHistory()
        {
            if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info) || info.StreamId <= 0)
                return;

            var messages = await _redisChatService.GetLastMessagesAsync(info.StreamId);
            await Clients.Caller.SendAsync("LoadChatHistory", messages);
        }
    }
}
