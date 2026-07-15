namespace StreamPlatformBackend.Models.Enums
{
    /// <summary>
    /// Global account roles. Not the same as channel team roles (StreamModerator).
    /// Moderator here means platform Trust &amp; Safety staff.
    /// </summary>
    public static class UserRole
    {
        public const string User = "User";
        public const string Support = "Support";
        public const string Moderator = "Moderator";
        public const string Admin = "Admin";
        public const string SuperAdmin = "SuperAdmin";

        public static readonly string[] StaffRoles =
        {
            Support,
            Moderator,
            Admin,
            SuperAdmin
        };

        public static readonly string[] PlatformModeratorRoles =
        {
            Moderator,
            Admin,
            SuperAdmin
        };

        public static readonly string[] AdminRoles =
        {
            Admin,
            SuperAdmin
        };

        public static bool IsStaff(string? role) =>
            !string.IsNullOrWhiteSpace(role) && StaffRoles.Contains(role);

        public static bool CanAccessStaffPanel(string? role) => IsStaff(role);

        public static bool CanManageTickets(string? role) => IsStaff(role);

        public static bool CanModeratePlatform(string? role) =>
            !string.IsNullOrWhiteSpace(role) && PlatformModeratorRoles.Contains(role);

        public static bool CanManageStaffRoles(string? role) =>
            !string.IsNullOrWhiteSpace(role) && AdminRoles.Contains(role);

        public static bool IsKnownRole(string? role) =>
            role is User or Support or Moderator or Admin or SuperAdmin;
    }
}
