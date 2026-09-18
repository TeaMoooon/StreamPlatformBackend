using System.Net;
using System.Net.Sockets;

namespace StreamPlatformBackend.Services
{
    /// <summary>Маскирует IP для отдачи клиенту (полный адрес остаётся только в БД).</summary>
    public static class IpAddressMask
    {
        /// <summary>
        /// IPv4: 192.168.1.100 → *.*.1.100
        /// IPv6: показывает только последний hextet.
        /// </summary>
        public static string Mask(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip) || ip.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return "unknown";

            var trimmed = ip.Trim();
            if (!IPAddress.TryParse(trimmed, out var address))
                return "*.*.*.*";

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var parts = trimmed.Split('.');
                if (parts.Length != 4)
                    return "*.*.*.*";
                return $"*.*.{parts[2]}.{parts[3]}";
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Collapse and keep last hextet only.
                var expanded = address.ToString();
                var hextets = expanded.Split(':');
                var last = hextets.Length > 0 ? hextets[^1] : "****";
                if (string.IsNullOrEmpty(last))
                    last = "****";
                return $"****:****:****:****:****:****:****:{last}";
            }

            return "*.*.*.*";
        }
    }
}
