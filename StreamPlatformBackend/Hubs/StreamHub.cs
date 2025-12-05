using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Hubs
{
    /// <summary>
    /// SignalR хаб для работы со стримами:
    /// - Подключение к стриму
    /// - Подсчет уникальных зрителей
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

        private readonly IRedisChatService _redisChatService;

        public StreamHub(IStreamService streamService, IUserService userService, IRedisChatService redisChatService, ILogger<StreamHub> logger)
        {
            _streamService = streamService;
            _userService = userService;
            _logger = logger;
            _redisChatService = redisChatService;

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
                await LoadChatHistory(streamerUsername);

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
        /// Обновление статуса стрима (IsLive) для всех зрителей
        /// </summary>
        /// <param name="streamerId">ID стримера</param>
        public async Task UpdateStreamStatus(int streamerId)
        {
            var streamInfo = await _streamService.GetStreamInfoAsync(streamerId);

            if (streamInfo == null)
            {
                // Стрим не найден или завершён
                await Clients.Group($"stream_{streamerId}")
                    .SendAsync("StreamStatusChanged", new
                    {
                        Status = "Offline",
                        Stream = (object?)null
                    });
                return;
            }

            string status = streamInfo.IsLive ? "Live" : "Offline";

            await Clients.Group($"stream_{streamerId}")
                .SendAsync("StreamStatusChanged", new
                {
                    Status = status,
                    Stream = streamInfo
                });
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



        // Новый метод — отправка сообщения
        public async Task SendChatMessage(string streamerUsername, string text)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer == null)
            {
                await Clients.Caller.SendAsync("Error", "Streamer not found");
                return;
            }

            var userId = GetCurrentUserId();
            var username = Context.User?.Identity?.Name ?? "Guest";

            // Определяем роль
            string role = "User";
            if (userId == streamer.Id) role = "Streamer";
            else if (await _redisChatService.IsModeratorAsync(streamer.Id, userId)) role = "Moderator";
            else if (Context.User.IsInRole("Admin") || Context.User.IsInRole("SuperAdmin"))
                role = "Admin";

            // Создаём DTO
            var streamInfo = await _streamService.GetStreamInfoAsync(streamer.Id);
            double offset = 0;
            if (streamInfo != null && streamInfo.StartedAt != null)
                offset = (DateTime.UtcNow - streamInfo.StartedAt.Value).TotalSeconds;

            var message = new ChatMessageDto
            {
                UserId = userId,
                Username = username,
                Text = text,
                Role = role,
                Timestamp = DateTime.UtcNow,
                OffsetSeconds = offset
            };

            // Сохраняем в Redis и публикуем
            await _redisChatService.AddMessageAsync(streamer.Id, message);
            await _redisChatService.PublishMessageAsync(streamer.Id, message);

            // Отправляем всем в группе
            await Clients.Group($"stream_{streamer.Id}").SendAsync("ReceiveChatMessage", message);
        }

        // Получение последних сообщений при заходе в стрим
        public async Task LoadChatHistory(string streamerUsername)
        {
            var streamer = await _userService.GetUserByNameAsync(streamerUsername);
            if (streamer == null) return;

            var messages = await _redisChatService.GetLastMessagesAsync(streamer.Id);
            await Clients.Caller.SendAsync("LoadChatHistory", messages);
        }
    }
}
