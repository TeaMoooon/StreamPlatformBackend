namespace StreamPlatformBackend.Constants
{
    public static class PlatformSanctionTypes
    {
        public const string Warning = "warning";
        public const string ChatMute = "chat_mute";
        public const string StreamBan = "stream_ban";
        public const string LoginBan = "login_ban";
        public const string FullBan = "full_ban";

        public static readonly string[] All =
        {
            Warning,
            ChatMute,
            StreamBan,
            LoginBan,
            FullBan
        };

        public static bool IsKnown(string? type) =>
            !string.IsNullOrWhiteSpace(type) && All.Contains(type);
    }

    public static class PlatformSanctionStatuses
    {
        public const string Active = "active";
        public const string Expired = "expired";
        public const string Revoked = "revoked";
    }
}
