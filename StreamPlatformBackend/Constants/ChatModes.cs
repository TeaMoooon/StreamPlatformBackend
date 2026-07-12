namespace StreamPlatformBackend.Constants
{
    public static class ChatModes
    {
        public const string Normal = "normal";
        public const string EmoteOnly = "emote_only";
        public const string SubscribersOnly = "subscribers_only";

        private static readonly HashSet<string> All = new(StringComparer.Ordinal)
        {
            Normal,
            EmoteOnly,
            SubscribersOnly
        };

        public static string Normalize(string? mode)
            => !string.IsNullOrWhiteSpace(mode) && All.Contains(mode) ? mode : Normal;

        public static string GetLabel(string mode) => Normalize(mode) switch
        {
            EmoteOnly => "Только эмодзи",
            SubscribersOnly => "Только для подписчиков",
            _ => "Обычный чат"
        };
    }
}
