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
    }

    public class StreamNotificationService : IStreamNotificationService
    {
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly ILogger<StreamNotificationService> _logger;

        public StreamNotificationService(IHubContext<StreamHub> hubContext, ILogger<StreamNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Отправка уведомлений о начале стрима всем подписанным пользователям
        /// </summary>
        public async Task NotifyStreamStartedAsync(StreamModel stream)
        {
            if (stream.User == null) return;

            // Отправляем всем подписчикам стримера
            await _hubContext.Clients.Group($"notifications_{stream.UserId}")
                .SendAsync("StreamStarted", new
                {
                    StreamId = stream.Id,
                    StreamName = stream.StreamName,
                    StreamerId = stream.UserId,
                    StreamerName = stream.User.Nickname,
                    Tags = stream.Tags,
                    PreviewlUrl = stream.PreviewUrl
                });

            _logger.LogInformation("Notified subscribers about stream start. StreamId: {StreamId}", stream.Id);
        }

        /// <summary>
        /// Отправка уведомлений о завершении стрима
        /// </summary>
        public async Task NotifyStreamEndedAsync(StreamModel stream)
        {
            if (stream.User == null) return;

            await _hubContext.Clients.Group($"notifications_{stream.UserId}")
                .SendAsync("StreamEnded", new
                {
                    StreamId = stream.Id,
                    StreamerId = stream.UserId
                });

            _logger.LogInformation("Notified subscribers about stream end. StreamId: {StreamId}", stream.Id);
        }
    }
}
