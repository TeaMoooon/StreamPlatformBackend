namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Periodically marks temporary platform sanctions as expired and notifies users.
    /// </summary>
    public class SanctionExpireHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SanctionExpireHostedService> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        public SanctionExpireHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<SanctionExpireHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Small delay so the app finishes starting.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sanctions = scope.ServiceProvider.GetRequiredService<IPlatformSanctionService>();
                    var expired = await sanctions.ExpireOverdueAsync();
                    if (expired > 0)
                        _logger.LogInformation("Expired {Count} temporary platform sanctions", expired);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sanction expire job failed");
                }

                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
