namespace StreamPlatformBackend.Constants
{
    public static class ReportTargetTypes
    {
        public const string User = "user";
        public const string Message = "message";
        public const string Channel = "channel";
        public const string Stream = "stream";

        public static readonly string[] All = { User, Message, Channel, Stream };

        public static bool IsKnown(string? type) =>
            !string.IsNullOrWhiteSpace(type) && All.Contains(type);
    }

    public static class ReportStatuses
    {
        public const string New = "new";
        public const string InProgress = "in_progress";
        public const string Resolved = "resolved";
        public const string Rejected = "rejected";

        public static readonly string[] All = { New, InProgress, Resolved, Rejected };

        public static bool IsKnown(string? status) =>
            !string.IsNullOrWhiteSpace(status) && All.Contains(status);
    }

    public static class ReportReasons
    {
        public const string Spam = "spam";
        public const string Harassment = "harassment";
        public const string Hate = "hate";
        public const string Illegal = "illegal";
        public const string Other = "other";

        public static readonly string[] All = { Spam, Harassment, Hate, Illegal, Other };

        public static bool IsKnown(string? reason) =>
            !string.IsNullOrWhiteSpace(reason) && All.Contains(reason);
    }
}
