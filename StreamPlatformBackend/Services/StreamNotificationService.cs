using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Models.Stream;


namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Сервис для отправки уведомлений о начале/конце стрима
    /// </summary>
    public interface IStreamNotificationService
    {
        Task NotifyStreamStartedAsync(StreamModel stream);
        Task NotifyStreamEndedAsync(StreamModel stream);
        Task NotifyStreamerSubscribersAsync(int streamerId, string message);
        Task NotifyUserAsync(int userId, string message, string type = "info");
    }


    public class StreamNotificationService : IStreamNotificationService
    {
        private readonly IHubContext<NotificationHub> _hub;

        public StreamNotificationService(IHubContext<NotificationHub> hub)
        {
            _hub = hub;
        }

        public async Task NotifyStreamStartedAsync(StreamModel stream)
        {
            await _hub.Clients.Group($"streamer_subs_{stream.UserId}")
                .SendAsync("ReceiveNotification", new
                {
                    Type = "stream_started",
                    Message = $"{stream.User.Nickname} начал стрим!",
                    StreamerId = stream.UserId,
                    StreamId = stream.Id,
                    StreamerName = stream.User.Nickname,  // <-- новое поле
                    stream.StreamName,            // <-- новое поле
                    Date = DateTime.UtcNow
                });
        }

        /*public async Task NotifyStreamEndedAsync(StreamModel stream)
        {
            await _hub.Clients.Group($"streamer_subs_{stream.UserId}")
                .SendAsync("ReceiveNotification", new
                {
                    Type = "stream_ended",
                    Message = $"{stream.User.Nickname} закончил стрим.",
                    StreamId = stream.Id,
                    StreamerId = stream.UserId,
                    Date = DateTime.UtcNow
                });
        }*/

        public Task NotifyStreamEndedAsync(StreamModel stream)
        {
            // Просто логируем или ничего не делаем
            return Task.CompletedTask;
        }

        public async Task NotifyStreamerSubscribersAsync(int streamerId, string message)
        {
            await _hub.Clients.Group($"streamer_subs_{streamerId}")
                .SendAsync("ReceiveNotification", new
                {
                    Type = "system",
                    Message = message,
                    StreamerId = streamerId,
                    Date = DateTime.UtcNow
                });
        }

        public async Task NotifyUserAsync(int userId, string message, string type = "info")
        {
            await _hub.Clients.Group($"user_{userId}")
                .SendAsync("ReceiveNotification", new
                {
                    Type = type,
                    Message = message,
                    Date = DateTime.UtcNow
                });
        }
    }
}

