using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public interface IStreamNotificationService
    {
        Task NotifyStreamStartedAsync(int streamerId, StreamModel stream);
        Task NotifyViewerCountChangedAsync(int streamerId, int viewerCount);
        Task NotifyStreamEndedAsync(int streamerId);
    }

    public class StreamNotificationService : IStreamNotificationService
    {
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly IUserService _userService;
        private readonly ILogger<StreamNotificationService> _logger;

        public StreamNotificationService(
            IHubContext<StreamHub> hubContext,
            IUserService userService,
            ILogger<StreamNotificationService> logger)
        {
            _hubContext = hubContext;
            _userService = userService;
            _logger = logger;
        }

        public async Task NotifyStreamStartedAsync(int streamerId, StreamModel stream)
        {
            try
            {
                var streamer = await _userService.GetUserByIdAsync(streamerId);
                if (streamer == null) return;

                // 1. Уведомляем всех на странице стрима
                var notifyViewersTask = _hubContext.Clients.Group($"stream_{streamerId}")
                    .SendAsync("StreamStarted", new
                    {
                        StreamId = stream.Id,
                        StreamName = stream.StreamName,
                        StreamerId = streamer.Id,
                        StreamerName = streamer.Nickname,
                        PreviewlUrl = stream.PreviewlUrl,
                        StartedAt = stream.StartedAt,
                        Tags = stream.Tags
                    });

                // 2. Уведомляем подписчиков (параллельно)
                var subscribers = await _userService.GetSubscribersAsync(streamerId);
                var notifySubscribersTasks = subscribers.Select(subscriber =>
                    _hubContext.Clients.Group($"notifications_{subscriber.Id}")
                        .SendAsync("StreamerStartedStream", new
                        {
                            StreamerId = streamerId,
                            StreamerName = streamer.Nickname,
                            StreamTitle = stream.StreamName,
                            PreviewUrl = stream.PreviewlUrl
                        }));

                // Запускаем все уведомления параллельно
                await Task.WhenAll(notifyViewersTask);
                await Task.WhenAll(notifySubscribersTasks);

                _logger.LogInformation("Stream start notifications sent for streamer {StreamerId} to {SubscriberCount} subscribers",
                    streamerId, subscribers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending stream start notifications for streamer {StreamerId}", streamerId);
            }
        }

        public async Task NotifyViewerCountChangedAsync(int streamerId, int viewerCount)
        {
            try
            {
                await _hubContext.Clients.Group($"stream_{streamerId}")
                    .SendAsync("ViewerCountUpdated", viewerCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending viewer count update for streamer {StreamerId}", streamerId);
            }
        }

        public async Task NotifyStreamEndedAsync(int streamerId)
        {
            try
            {
                await _hubContext.Clients.Group($"stream_{streamerId}")
                    .SendAsync("StreamEnded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending stream end notification for streamer {StreamerId}", streamerId);
            }
        }
    }
}