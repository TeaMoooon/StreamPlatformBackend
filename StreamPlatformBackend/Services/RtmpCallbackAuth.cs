using System.Security.Cryptography;
using System.Text;

namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Shared-secret auth for nginx-rtmp / SRS HTTP notify callbacks.
    /// Secret must arrive via header only — never query string (logs / process lists).
    /// </summary>
    public static class RtmpCallbackAuth
    {
        public const string HeaderName = "X-Rtmp-Secret";

        public static bool IsConfigured(string? configuredSecret) =>
            !string.IsNullOrWhiteSpace(configuredSecret) &&
            !LocalSecretsGuard.IsInsecurePlaceholder(configuredSecret);

        public static string? ReadProvidedSecret(HttpRequest request)
        {
            if (request.Headers.TryGetValue(HeaderName, out var values))
            {
                var header = values.ToString();
                if (!string.IsNullOrEmpty(header))
                    return header;
            }

            return null;
        }

        public static bool IsLoopback(HttpRequest request)
        {
            var ip = request.HttpContext.Connection.RemoteIpAddress;
            return ip != null && System.Net.IPAddress.IsLoopback(ip);
        }

        public static bool SecretsMatch(string configuredSecret, string? providedSecret)
        {
            if (string.IsNullOrEmpty(providedSecret))
                return false;

            var expected = Encoding.UTF8.GetBytes(configuredSecret);
            var actual = Encoding.UTF8.GetBytes(providedSecret);
            return expected.Length == actual.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, actual);
        }
    }
}
