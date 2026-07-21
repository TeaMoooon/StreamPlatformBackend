namespace StreamPlatformBackend.Constants
{
    public static class AppealStatuses
    {
        public const string Open = "open";
        public const string InReview = "in_review";
        public const string Approved = "approved";
        public const string Rejected = "rejected";

        public static readonly string[] All = { Open, InReview, Approved, Rejected };
        public static readonly string[] OpenLike = { Open, InReview };

        public static bool IsKnown(string? status) =>
            !string.IsNullOrWhiteSpace(status) && All.Contains(status);
    }
}
