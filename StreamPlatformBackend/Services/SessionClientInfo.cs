namespace StreamPlatformBackend.Services
{
    public sealed class SessionClientInfo
    {
        public string IpAddress { get; init; } = "unknown";
        public string DeviceLabel { get; init; } = "Неизвестно";
        public string DeviceCategory { get; init; } = "Unknown";

        public static SessionClientInfo FromRequest(HttpRequest request) => new()
        {
            IpAddress = ClientRequestInfo.GetIpAddress(request),
            DeviceLabel = ClientRequestInfo.GetDeviceType(request),
            DeviceCategory = ClientRequestInfo.GetDeviceCategory(request)
        };
    }
}
