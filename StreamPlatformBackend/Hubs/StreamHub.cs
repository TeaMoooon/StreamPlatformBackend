using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Helpers;
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
        private readonly IStreamChatBanService _streamChatBanService;
        private readonly IStreamChatModerationLogService _moderationLogService;
        private readonly ILogger<StreamHub> _logger;

        // Потокобезопасные коллекции
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> StreamViewers = new();
        private static readonly ConcurrentDictionary<string, (int StreamerId, string ViewerKey, int StreamId)> ConnectionMap = new();

        public StreamHub(
            IStreamService streamService,
            IUserService userService,
            IRedisChatService redisChatService,
            IStreamChatBanService streamChatBanService,
            IStreamChatModerationLogService moderationLogService,
            ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _redisChatService = redisChatService;
            _streamChatBanService = streamChatBanService;
            _moderationLogService = moderationLogService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out int userId) ? userId : 0;
        }

        private async Task<bool> CanManageChatAsync(int userId, int streamerId)
        {
            if (userId <= 0) return false;
            if (userId == streamerId) return true;
            if (await _streamChatBanService.IsBannedAsync(streamerId, userId))
                return false;
            if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
                return true;
            return await _redisChatService.IsChatManagerAsync(streamerId, userId);
        }

        private async Task<string> GetUserRoleAsync(int userId, int streamerId)
        {
            if (userId == streamerId) return "Streamer";
            if (await _redisChatService.IsModeratorAsync(streamerId, userId)) return "Moderator";
            if (await _redisChatService.IsAssistantAsync(streamerId, userId)) return "Assistant";
            if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SuperAdmin") == true)
                return "Admin";
            return "User";
        }

        private bool CanModerateTarget(string actorRole, string targetRole, int actorId, int targetId, int streamerId)
        {
            if (actorId == targetId) return false;
            if (targetId == streamerId) return false;
            if (targetRole is "Streamer" or "Admin") return false;
            if (targetRole == "Moderator" && actorRole != "Streamer" && actorRole != "Admin")
                return false;
            if (targetRole == "Assistant" && actorRole == "Assistant")
                return false;
            return true;
        }

        private async Task<string> GetTeamMemberRoleAsync(int userId, int streamerId)
        {
            if (userId == streamerId) return "Streamer";
            if (await _redisChatService.IsModeratorAsync(streamerId, userId)) return "Moderator";
            if (await _redisChatService.IsAssistantAsync(streamerId, userId)) return "Assistant";
            return "User";
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
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                if (info.StreamId <= 0)
                {
                    await Clients.Caller.SendAsync("Error", "ChatStreamOffline");
                    return;
                }

                int userId = GetCurrentUserId();
                string username = Context.User.Identity?.Name ?? "Unknown";
                int streamerId = info.StreamerId;

                var trimmed = (text ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    await Clients.Caller.SendAsync("Error", "ChatMessageEmpty");
                    return;
                }

                if (trimmed.Length > ChatConstants.MaxMessageLength)
                {
                    await Clients.Caller.SendAsync("Error", "ChatMessageTooLong");
                    return;
                }

                string role = "User";
                if (userId == streamerId) role = "Streamer";
                else if (await _redisChatService.IsModeratorAsync(streamerId, userId)) role = "Moderator";
                else if (await _redisChatService.IsAssistantAsync(streamerId, userId)) role = "Assistant";
                else if (Context.User.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin"))
                    role = "Admin";

                var bypassSlowMode = role is "Streamer" or "Moderator" or "Assistant" or "Admin";
                var bypassChatMode = bypassSlowMode;
                if (await _streamChatBanService.IsBannedAsync(streamerId, userId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatBanned");
                    return;
                }

                var timeoutRemaining = await _redisChatService.GetTimeoutRemainingAsync(streamerId, userId);
                if (timeoutRemaining.HasValue && role == "User")
                {
                    await Clients.Caller.SendAsync("Error", $"ChatTimedOut:{timeoutRemaining.Value}");
                    return;
                }

                var slowModeSeconds = await _redisChatService.GetSlowModeSecondsAsync(streamerId);
                var waitSeconds = await _redisChatService.CheckSlowModeAsync(streamerId, userId, bypassSlowMode);
                if (waitSeconds.HasValue)
                {
                    await Clients.Caller.SendAsync("Error", $"ChatSlowMode:{waitSeconds.Value}");
                    return;
                }

                var chatMode = await _redisChatService.GetChatModeAsync(streamerId);
                if (!bypassChatMode)
                {
                    if (chatMode == ChatModes.SubscribersOnly)
                    {
                        var isSubscribed = await _userService.IsSubscribedAsync(userId, streamerId);
                        if (!isSubscribed)
                        {
                            await Clients.Caller.SendAsync("Error", "ChatSubscribersOnly");
                            return;
                        }
                    }

                    if (chatMode == ChatModes.EmoteOnly && !ChatMessageValidator.IsEmoteOnlyMessage(trimmed))
                    {
                        await Clients.Caller.SendAsync("Error", "ChatEmoteOnly");
                        return;
                    }
                }

                var streamInfo = await _streamService.GetStreamInfoAsync(streamerId);
                double offset = streamInfo?.StartedAt != null
                    ? (DateTime.UtcNow - streamInfo.StartedAt.Value).TotalSeconds
                    : 0;

                var message = new ChatMessageDto
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = userId,
                    Username = username,
                    Text = trimmed,
                    Role = role,
                    Timestamp = DateTime.UtcNow,
                    OffsetSeconds = offset
                };

                await _redisChatService.AddMessageAsync(info.StreamId, message);
                await _redisChatService.PublishMessageAsync(info.StreamId, message);
                await _redisChatService.RegisterMessageSentAsync(streamerId, userId, slowModeSeconds);

                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ReceiveChatMessage", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendChatMessage");
                await Clients.Caller.SendAsync("Error", "ChatSendFailed");
            }
        }

        /// <summary>Фаза 1: настройка slow mode (UI — в следующих этапах). Только стример/мод/admin.</summary>
        public async Task SetChatSlowMode(int seconds)
        {
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                int userId = GetCurrentUserId();
                int streamerId = info.StreamerId;

                var canManage = await CanManageChatAsync(userId, streamerId);

                if (!canManage)
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                seconds = Math.Clamp(seconds, 0, ChatConstants.MaxSlowModeSeconds);
                var previous = await _redisChatService.GetSlowModeSecondsAsync(streamerId);
                await _redisChatService.SetSlowModeSecondsAsync(streamerId, seconds);

                if (seconds != previous)
                {
                    await _moderationLogService.LogAsync(
                        streamerId,
                        userId,
                        ChatModerationActions.SlowMode,
                        details: seconds == 0 ? "Выключен" : $"{seconds} сек");
                }

                var chatRules = await _redisChatService.GetChatRulesAsync(streamerId);
                var chatMode = await _redisChatService.GetChatModeAsync(streamerId);
                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatSettingsChanged", new { slowModeSeconds = seconds, chatRules, chatMode });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SetChatSlowMode");
                await Clients.Caller.SendAsync("Error", "ChatSlowModeFailed");
            }
        }

        public async Task DeleteChatMessage(string messageId)
        {
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info) || info.StreamId <= 0)
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                int userId = GetCurrentUserId();
                int streamerId = info.StreamerId;

                if (!await CanManageChatAsync(userId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                if (string.IsNullOrWhiteSpace(messageId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatMessageNotFound");
                    return;
                }

                var messages = await _redisChatService.GetLastMessagesAsync(info.StreamId);
                var target = messages.FirstOrDefault(m => m.Id == messageId);
                if (target == null)
                {
                    await Clients.Caller.SendAsync("Error", "ChatMessageNotFound");
                    return;
                }

                var actorRole = await GetUserRoleAsync(userId, streamerId);
                if (!CanModerateTarget(actorRole, target.Role, userId, target.UserId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                var deletedText = target.Text;
                var marked = await _redisChatService.MarkMessageDeletedAsync(info.StreamId, messageId);
                if (!marked)
                {
                    await Clients.Caller.SendAsync("Error", "ChatMessageNotFound");
                    return;
                }

                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatMessageDeleted", new
                    {
                        messageId,
                        userId = target.UserId,
                        username = target.Username,
                        timestamp = target.Timestamp,
                        role = target.Role,
                        deletedText
                    });

                await _moderationLogService.LogAsync(
                    streamerId,
                    userId,
                    ChatModerationActions.Delete,
                    target.UserId,
                    target.Username,
                    messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeleteChatMessage");
                await Clients.Caller.SendAsync("Error", "ChatDeleteFailed");
            }
        }

        public async Task TimeoutChatUser(int targetUserId, int seconds)
        {
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                int userId = GetCurrentUserId();
                int streamerId = info.StreamerId;

                if (!await CanManageChatAsync(userId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                if (targetUserId <= 0)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var targetUser = await _userService.GetUserByIdAsync(targetUserId);
                if (targetUser == null)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var actorRole = await GetUserRoleAsync(userId, streamerId);
                var targetRole = await GetTeamMemberRoleAsync(targetUserId, streamerId);

                if (!CanModerateTarget(actorRole, targetRole, userId, targetUserId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                seconds = Math.Clamp(seconds, 0, ChatConstants.MaxTimeoutSeconds);
                await _redisChatService.SetTimeoutAsync(streamerId, targetUserId, seconds);

                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatUserTimedOut", new
                    {
                        userId = targetUserId,
                        username = targetUser.Nickname,
                        seconds
                    });

                await _moderationLogService.LogAsync(
                    streamerId,
                    userId,
                    ChatModerationActions.Timeout,
                    targetUserId,
                    targetUser.Nickname,
                    details: $"{seconds} сек");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TimeoutChatUser");
                await Clients.Caller.SendAsync("Error", "ChatTimeoutFailed");
            }
        }

        public async Task BanChatUser(int targetUserId)
        {
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                int userId = GetCurrentUserId();
                int streamerId = info.StreamerId;

                if (!await CanManageChatAsync(userId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                if (targetUserId <= 0)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var targetUser = await _userService.GetUserByIdAsync(targetUserId);
                if (targetUser == null)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var actorRole = await GetUserRoleAsync(userId, streamerId);
                var targetRole = await GetTeamMemberRoleAsync(targetUserId, streamerId);

                if (!CanModerateTarget(actorRole, targetRole, userId, targetUserId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                await _streamChatBanService.BanAsync(streamerId, targetUserId, userId);

                if (info.StreamId > 0)
                    await _redisChatService.MarkUserMessagesDeletedAsync(info.StreamId, targetUserId);

                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatUserBanned", new
                    {
                        userId = targetUserId,
                        username = targetUser.Nickname,
                        removedFromTeam = true
                    });

                await _moderationLogService.LogAsync(
                    streamerId,
                    userId,
                    ChatModerationActions.Ban,
                    targetUserId,
                    targetUser.Nickname);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BanChatUser");
                await Clients.Caller.SendAsync("Error", "ChatBanFailed");
            }
        }

        public async Task UnbanChatUser(int targetUserId)
        {
            try
            {
                if (Context.User?.Identity?.IsAuthenticated != true)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUnauthorized");
                    return;
                }

                if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info))
                {
                    await Clients.Caller.SendAsync("Error", "ChatNotJoined");
                    return;
                }

                int userId = GetCurrentUserId();
                int streamerId = info.StreamerId;

                if (!await CanManageChatAsync(userId, streamerId))
                {
                    await Clients.Caller.SendAsync("Error", "ChatForbidden");
                    return;
                }

                if (targetUserId <= 0)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var targetUser = await _userService.GetUserByIdAsync(targetUserId);
                if (targetUser == null)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotFound");
                    return;
                }

                var unbanned = await _streamChatBanService.UnbanAsync(streamerId, targetUserId);
                if (!unbanned)
                {
                    await Clients.Caller.SendAsync("Error", "ChatUserNotBanned");
                    return;
                }

                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatUserUnbanned", new
                    {
                        userId = targetUserId,
                        username = targetUser.Nickname
                    });

                await _moderationLogService.LogAsync(
                    streamerId,
                    userId,
                    ChatModerationActions.Unban,
                    targetUserId,
                    targetUser.Nickname);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UnbanChatUser");
                await Clients.Caller.SendAsync("Error", "ChatUnbanFailed");
            }
        }

        public async Task LoadChatHistory()
        {
            if (!ConnectionMap.TryGetValue(Context.ConnectionId, out var info) || info.StreamId <= 0)
                return;

            var userId = GetCurrentUserId();
            var messages = await _redisChatService.GetLastMessagesAsync(info.StreamId);
            var slowModeSeconds = await _redisChatService.GetSlowModeSecondsAsync(info.StreamerId);
            var chatRules = await _redisChatService.GetChatRulesAsync(info.StreamerId);
            var chatMode = await _redisChatService.GetChatModeAsync(info.StreamerId);
            var canManageChat = await CanManageChatAsync(userId, info.StreamerId);
            var canSendChat = await CanSendChatAsync(userId, info.StreamerId, canManageChat, chatMode);
            var clientMessages = messages.Select(m => MapMessageForClient(m, canManageChat)).ToList();
            var bannedUserIds = canManageChat
                ? await _streamChatBanService.GetBannedUserIdsAsync(info.StreamerId)
                : null;
            await Clients.Caller.SendAsync("LoadChatHistory", clientMessages);
            await Clients.Caller.SendAsync("ChatSettingsChanged", new
            {
                slowModeSeconds,
                chatRules,
                chatMode,
                canManageChat,
                canSendChat,
                bannedUserIds
            });
        }

        private async Task<bool> CanSendChatAsync(int userId, int streamerId, bool canManageChat, string chatMode)
        {
            if (userId <= 0) return false;
            if (canManageChat || userId == streamerId) return true;
            if (chatMode != ChatModes.SubscribersOnly) return true;
            return await _userService.IsSubscribedAsync(userId, streamerId);
        }

        private static object MapMessageForClient(ChatMessageDto message, bool canManageChat)
        {
            if (!message.IsDeleted)
            {
                return new
                {
                    id = message.Id,
                    userId = message.UserId,
                    username = message.Username,
                    text = message.Text,
                    role = message.Role,
                    timestamp = message.Timestamp,
                    offsetSeconds = message.OffsetSeconds,
                    isDeleted = false
                };
            }

            return new
            {
                id = message.Id,
                userId = message.UserId,
                username = message.Username,
                text = string.Empty,
                role = message.Role,
                timestamp = message.Timestamp,
                offsetSeconds = message.OffsetSeconds,
                isDeleted = true,
                deletedText = canManageChat ? message.Text : null
            };
        }
    }
}
