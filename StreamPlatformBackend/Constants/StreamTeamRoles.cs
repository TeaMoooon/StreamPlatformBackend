namespace StreamPlatformBackend.Constants
{
    public static class StreamTeamRoles
    {
        public const string Moderator = "Moderator";
        public const string Assistant = "Assistant";

        public static bool IsValid(string? role) =>
            role == Moderator || role == Assistant;
    }
}
