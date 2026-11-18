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

        /// <summary>List filter alias: open queue = new + in_progress. Not stored on rows.</summary>
        public const string Open = "open";

        public static readonly string[] All = { New, InProgress, Resolved, Rejected };
        public static readonly string[] OpenStatuses = { New, InProgress };

        public static bool IsKnown(string? status) =>
            !string.IsNullOrWhiteSpace(status) && All.Contains(status);

        public static bool IsListFilter(string? status) =>
            IsKnown(status) || string.Equals(status, Open, StringComparison.OrdinalIgnoreCase);
    }

    public static class ReportReasons
    {
        // Legacy bucket — kept so old rows / clients remain valid.
        public const string Illegal = "illegal";

        public const string ChildSafety = "child_safety";
        public const string Terrorism = "terrorism";
        public const string Violence = "violence";
        public const string Sexual = "sexual";
        public const string SelfHarm = "self_harm";
        public const string Harassment = "harassment";
        public const string Hate = "hate";
        public const string Spam = "spam";
        public const string WrongCategory = "wrong_category";
        public const string Other = "other";

        public static readonly string[] All =
        {
            ChildSafety,
            Terrorism,
            Violence,
            Sexual,
            SelfHarm,
            Harassment,
            Hate,
            Spam,
            WrongCategory,
            Other,
            Illegal,
        };

        public static bool IsKnown(string? reason) =>
            !string.IsNullOrWhiteSpace(reason) && All.Contains(reason);
    }
}
