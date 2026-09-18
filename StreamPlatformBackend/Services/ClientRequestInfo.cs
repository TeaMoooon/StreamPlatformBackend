using System.Net;

namespace StreamPlatformBackend.Services
{
    /// <summary>IP и описание устройства из HTTP-запроса (за nginx / CDN).</summary>
    public static class ClientRequestInfo
    {
        public static string GetIpAddress(HttpRequest request)
        {
            var forwarded = request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (IsPlausibleIp(first))
                    return Truncate(first, 64);
            }

            var realIp = request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
            {
                var trimmed = realIp.Trim();
                if (IsPlausibleIp(trimmed))
                    return Truncate(trimmed, 64);
            }

            var remote = request.HttpContext.Connection.RemoteIpAddress;
            if (remote != null)
            {
                if (remote.IsIPv4MappedToIPv6)
                    remote = remote.MapToIPv4();
                return Truncate(remote.ToString(), 64);
            }

            return "unknown";
        }

        /// <summary>
        /// Человекочитаемая метка: «Chrome · Windows», «Safari · iPhone».
        /// По User-Agent — без внешних библиотек; VPN/боты могут врать.
        /// </summary>
        public static string GetDeviceType(HttpRequest request)
        {
            var ua = request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua))
                return "Неизвестно";

            var browser = DetectBrowser(ua);
            var os = DetectOs(ua);
            var label = $"{browser} · {os}";
            return Truncate(label, 96);
        }

        /// <summary>Desktop | Mobile | Tablet | Unknown — для группировки сеансов.</summary>
        public static string GetDeviceCategory(HttpRequest request)
        {
            var ua = request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(ua))
                return "Unknown";

            var lower = ua.ToLowerInvariant();

            if (lower.Contains("ipad") ||
                lower.Contains("tablet") ||
                (lower.Contains("android") && !lower.Contains("mobile")))
                return "Tablet";

            if (lower.Contains("mobi") ||
                lower.Contains("iphone") ||
                lower.Contains("ipod") ||
                lower.Contains("android") ||
                lower.Contains("windows phone"))
                return "Mobile";

            return "Desktop";
        }

        private static string DetectBrowser(string ua)
        {
            // Order matters: Edge/Opera/Samsung before Chrome; Chrome before Safari.
            if (Contains(ua, "Edg/") || Contains(ua, "EdgA/") || Contains(ua, "EdgiOS/"))
                return "Edge";
            if (Contains(ua, "OPR/") || Contains(ua, "Opera"))
                return "Opera";
            if (Contains(ua, "SamsungBrowser/"))
                return "Samsung Internet";
            if (Contains(ua, "Firefox/") || Contains(ua, "FxiOS/"))
                return "Firefox";
            if (Contains(ua, "YaBrowser/"))
                return "Яндекс";
            if (Contains(ua, "Chrome/") || Contains(ua, "CriOS/"))
                return "Chrome";
            if (Contains(ua, "Safari/") && Contains(ua, "Version/"))
                return "Safari";
            if (Contains(ua, "MSIE") || Contains(ua, "Trident/"))
                return "IE";
            return "Браузер";
        }

        private static string DetectOs(string ua)
        {
            if (Contains(ua, "iPhone"))
                return "iPhone";
            if (Contains(ua, "iPad"))
                return "iPad";
            if (Contains(ua, "iPod"))
                return "iPod";
            if (Contains(ua, "Android"))
                return Contains(ua, "Mobile") ? "Android" : "Android (планшет)";
            if (Contains(ua, "Windows Phone"))
                return "Windows Phone";
            if (Contains(ua, "Windows NT"))
                return "Windows";
            if (Contains(ua, "Mac OS X") || Contains(ua, "Macintosh"))
                return "macOS";
            if (Contains(ua, "CrOS"))
                return "ChromeOS";
            if (Contains(ua, "Linux"))
                return "Linux";
            return "устройство";
        }

        private static bool Contains(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        private static bool IsPlausibleIp(string value) =>
            IPAddress.TryParse(value, out _);

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
