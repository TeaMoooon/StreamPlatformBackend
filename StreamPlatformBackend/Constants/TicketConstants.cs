namespace StreamPlatformBackend.Constants
{
    public static class TicketCategories
    {
        public const string Account = "account";
        public const string Stream = "stream";
        public const string Payments = "payments";
        public const string Abuse = "abuse";
        public const string Other = "other";

        public static readonly string[] All = { Account, Stream, Payments, Abuse, Other };

        public static bool IsKnown(string? category) =>
            !string.IsNullOrWhiteSpace(category) && All.Contains(category);
    }

    public static class TicketStatuses
    {
        public const string Open = "open";
        public const string InProgress = "in_progress";
        public const string WaitingUser = "waiting_user";
        public const string Resolved = "resolved";
        public const string Closed = "closed";

        public static readonly string[] All = { Open, InProgress, WaitingUser, Resolved, Closed };

        public static bool IsKnown(string? status) =>
            !string.IsNullOrWhiteSpace(status) && All.Contains(status);

        public static bool IsOpenLike(string? status) =>
            status is Open or InProgress or WaitingUser;
    }
}
