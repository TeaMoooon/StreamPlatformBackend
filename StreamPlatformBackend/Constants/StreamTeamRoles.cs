namespace StreamPlatformBackend.Constants
{
    /// <summary>
    /// Roles inside a streamer's channel team (<c>StreamModerator</c>).
    /// Not the same as platform account roles in <c>UserRole</c> (Support / Safety / Admin / Owner).
    /// </summary>
    public static class StreamTeamRoles
    {
        public const string Moderator = "Moderator";
        public const string Assistant = "Assistant";

        public static bool IsValid(string? role) =>
            role == Moderator || role == Assistant;
    }
}
