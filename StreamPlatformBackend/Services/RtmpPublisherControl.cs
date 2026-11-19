using System.Net.Http;

namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Asks nginx-rtmp (local control API) to drop an active publisher so rotate/ban
    /// does not leave a zombie RTMP session under the old stream key/name.
    /// </summary>
    public interface IRtmpPublisherControl
    {
        Task TryDropPublisherAsync(string streamKey, CancellationToken ct = default);
    }

    public sealed class RtmpPublisherControl : IRtmpPublisherControl
    {
        private readonly HttpClient _http;
        private readonly ILogger<RtmpPublisherControl> _logger;
        private readonly string? _dropUrlTemplate;

        public RtmpPublisherControl(
            HttpClient http,
            IConfiguration configuration,
            ILogger<RtmpPublisherControl> logger)
        {
            _http = http;
            _logger = logger;
            // Example: http://127.0.0.1:5155/control/drop/publisher?app=live&name={0}
            _dropUrlTemplate = configuration["Rtmp:DropPublisherUrl"];
        }

        public async Task TryDropPublisherAsync(string streamKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(streamKey) || string.IsNullOrWhiteSpace(_dropUrlTemplate))
                return;

            if (!System.Text.RegularExpressions.Regex.IsMatch(streamKey, @"^[A-Za-z0-9_-]+$"))
            {
                _logger.LogWarning("Refusing RTMP drop for unsafe stream key");
                return;
            }

            try
            {
                var url = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    _dropUrlTemplate,
                    Uri.EscapeDataString(streamKey));

                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "RTMP drop publisher returned {Status} for {StreamKey}",
                        (int)response.StatusCode,
                        MaskKey(streamKey));
                    return;
                }

                _logger.LogInformation("RTMP publisher dropped for {StreamKey}", MaskKey(streamKey));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "RTMP drop publisher failed for {StreamKey}", MaskKey(streamKey));
            }
        }

        private static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 10)
                return "***";
            return key[..8] + "…";
        }
    }

    /// <summary>No-op control used in unit tests / when drop URL is unset.</summary>
    public sealed class NullRtmpPublisherControl : IRtmpPublisherControl
    {
        public static readonly NullRtmpPublisherControl Instance = new();
        public Task TryDropPublisherAsync(string streamKey, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
