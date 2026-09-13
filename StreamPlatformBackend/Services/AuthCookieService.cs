using Microsoft.AspNetCore.Http;

namespace StreamPlatformBackend.Services
{
    public static class AuthCookieNames
    {
        /// <summary>HttpOnly JWT access token.</summary>
        public const string Access = "sp_access";

        /// <summary>HttpOnly opaque refresh token.</summary>
        public const string Refresh = "sp_refresh";

        /// <summary>
        /// Non-HttpOnly session marker so the SPA can detect login without reading JWTs.
        /// Value is always "1" — never a secret.
        /// </summary>
        public const string Session = "sp_auth";
    }

    public interface IAuthCookieService
    {
        void AppendAuthCookies(HttpResponse response, HttpRequest request, AuthTokenPair pair);
        void ClearAuthCookies(HttpResponse response, HttpRequest request);
        string? ReadAccessToken(HttpRequest request);
        string? ReadRefreshToken(HttpRequest request);
    }

    public sealed class AuthCookieService : IAuthCookieService
    {
        private readonly IConfiguration _configuration;

        public AuthCookieService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void AppendAuthCookies(HttpResponse response, HttpRequest request, AuthTokenPair pair)
        {
            var accessSeconds = Math.Max(60, pair.ExpiresInSeconds);
            var refreshDays = Math.Max(1, _configuration.GetValue("Jwt:RefreshTokenDays", 30));

            response.Cookies.Append(AuthCookieNames.Access, pair.AccessToken, BuildOptions(request, httpOnly: true, accessSeconds));
            response.Cookies.Append(AuthCookieNames.Refresh, pair.RefreshToken, BuildOptions(request, httpOnly: true, (int)TimeSpan.FromDays(refreshDays).TotalSeconds));
            response.Cookies.Append(AuthCookieNames.Session, "1", BuildOptions(request, httpOnly: false, (int)TimeSpan.FromDays(refreshDays).TotalSeconds));
        }

        public void ClearAuthCookies(HttpResponse response, HttpRequest request)
        {
            var expired = BuildOptions(request, httpOnly: true, maxAgeSeconds: 0);
            expired.Expires = DateTimeOffset.UnixEpoch;

            response.Cookies.Append(AuthCookieNames.Access, string.Empty, expired);

            var refreshExpired = BuildOptions(request, httpOnly: true, maxAgeSeconds: 0);
            refreshExpired.Expires = DateTimeOffset.UnixEpoch;
            response.Cookies.Append(AuthCookieNames.Refresh, string.Empty, refreshExpired);

            var sessionExpired = BuildOptions(request, httpOnly: false, maxAgeSeconds: 0);
            sessionExpired.Expires = DateTimeOffset.UnixEpoch;
            response.Cookies.Append(AuthCookieNames.Session, string.Empty, sessionExpired);
        }

        public string? ReadAccessToken(HttpRequest request) =>
            request.Cookies.TryGetValue(AuthCookieNames.Access, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        public string? ReadRefreshToken(HttpRequest request) =>
            request.Cookies.TryGetValue(AuthCookieNames.Refresh, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        private CookieOptions BuildOptions(HttpRequest request, bool httpOnly, int maxAgeSeconds)
        {
            var sameSite = ParseSameSite(_configuration["AuthCookies:SameSite"]);
            var secureConfig = _configuration["AuthCookies:Secure"];
            var secure = secureConfig switch
            {
                "true" => true,
                "false" => false,
                _ => request.IsHttps || string.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase)
            };

            // SameSite=None requires Secure; fall back to Lax on plain HTTP.
            if (sameSite == SameSiteMode.None && !secure)
                sameSite = SameSiteMode.Lax;

            return new CookieOptions
            {
                HttpOnly = httpOnly,
                Secure = secure,
                SameSite = sameSite,
                Path = "/",
                IsEssential = true,
                MaxAge = maxAgeSeconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(maxAgeSeconds)
            };
        }

        private static SameSiteMode ParseSameSite(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "none" => SameSiteMode.None,
                "strict" => SameSiteMode.Strict,
                _ => SameSiteMode.Lax
            };
    }
}
