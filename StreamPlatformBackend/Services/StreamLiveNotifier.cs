using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Hubs;

namespace StreamPlatformBackend.Services
{
    public interface IStreamLiveNotifier
    {
        Task PublishStatusAsync(int streamerId, StreamInfoDto? streamInfo);
        void ScheduleRepublish(int streamerId, params TimeSpan[] delays);
    }

    public class StreamLiveNotifier : IStreamLiveNotifier
    {
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StreamLiveNotifier> _logger;

        public StreamLiveNotifier(
            IHubContext<StreamHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<StreamLiveNotifier> logger)
        {
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task PublishStatusAsync(int streamerId, StreamInfoDto? streamInfo)
        {
            try
            {
                var isLive = streamInfo?.IsLive == true;
                await _hubContext.Clients.Group($"stream_{streamerId}")
                    .SendAsync("StreamStatusChanged", new
                    {
                        Status = isLive ? "Live" : "Offline",
                        Stream = streamInfo
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish stream status for streamer {StreamerId}", streamerId);
            }
        }

        public void ScheduleRepublish(int streamerId, params TimeSpan[] delays)
        {
            foreach (var delay in delays)
            {
                _ = RepublishAfterDelayAsync(streamerId, delay);
            }
        }

        private async Task RepublishAfterDelayAsync(int streamerId, TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay);

                using var scope = _scopeFactory.CreateScope();
                var streamService = scope.ServiceProvider.GetRequiredService<IStreamService>();
                var notifier = scope.ServiceProvider.GetRequiredService<IStreamLiveNotifier>();

                var info = await streamService.GetStreamInfoAsync(streamerId);
                if (info?.IsLive != true)
                    return;

                await notifier.PublishStatusAsync(streamerId, info);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Delayed stream status republish failed for streamer {StreamerId}", streamerId);
            }
        }
    }
}
