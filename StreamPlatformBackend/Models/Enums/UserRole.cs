namespace StreamPlatformBackend.Models.Enums
{
    /// <summary>
    /// Global account roles for the platform (staff / Trust &amp; Safety / support).
    /// Not the same as channel team roles in <c>StreamModerator</c> / <c>StreamTeamRoles</c>
    /// (those stay "Moderator" / "Assistant" for a streamer's chat team).
    /// </summary>
    public static class UserRole
    {
        public const string User = "User";
        public const string Support = "Support";
        public const string SupportLead = "SupportLead";
        public const string Safety = "Safety";
        public const string SafetyLead = "SafetyLead";
        public const string Admin = "Admin";
        public const string Owner = "Owner";

        /// <summary>Legacy DB/JWT value; normalize to <see cref="Safety"/>.</summary>
        public const string LegacyModerator = "Moderator";

        /// <summary>Legacy DB/JWT value; normalize to <see cref="Owner"/>.</summary>
        public const string LegacySuperAdmin = "SuperAdmin";

        public static readonly string[] All =
        {
            User,
            Support,
            SupportLead,
            Safety,
            SafetyLead,
            Admin,
            Owner
        };

        public static readonly string[] StaffRoles =
        {
            Support,
            SupportLead,
            Safety,
            SafetyLead,
            Admin,
            Owner,
            // JWT claims until re-login after migration:
            LegacyModerator,
            LegacySuperAdmin
        };

        /// <summary>Can work Trust &amp; Safety queues (reports, appeals, sanctions).</summary>
        public static readonly string[] PlatformSafetyRoles =
        {
            Safety,
            SafetyLead,
            Admin,
            Owner,
            LegacyModerator,
            LegacySuperAdmin
        };

        /// <summary>Can assign staff roles (Admin+) — see <see cref="CanAssignRole"/> for scope.</summary>
        public static readonly string[] AdminRoles =
        {
            Admin,
            Owner,
            LegacySuperAdmin
        };

        /// <summary>Backward-compatible alias used by auth policies.</summary>
        public static readonly string[] PlatformModeratorRoles = PlatformSafetyRoles;

        public static string Normalize(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return User;

            var trimmed = role.Trim();
            if (trimmed == LegacyModerator)
                return Safety;
            if (trimmed == LegacySuperAdmin)
                return Owner;
            return trimmed;
        }

        /// <summary>Higher number = more authority.</summary>
        public static int Rank(string? role) => Normalize(role) switch
        {
            User => 0,
            Support => 10,
            SupportLead => 20,
            Safety => 10,
            SafetyLead => 20,
            Admin => 50,
            Owner => 100,
            _ => -1
        };

        public static bool IsStaff(string? role)
        {
            var normalized = Normalize(role);
            return StaffRoles.Contains(normalized);
        }

        public static bool CanAccessStaffPanel(string? role) => IsStaff(role);

        public static bool CanManageTickets(string? role) => IsStaff(role);

        public static bool CanModeratePlatform(string? role)
        {
            var normalized = Normalize(role);
            return PlatformSafetyRoles.Contains(normalized);
        }

        public static bool CanManageStaffRoles(string? role)
        {
            var normalized = Normalize(role);
            return AdminRoles.Contains(normalized);
        }

        public static bool IsSupportDepartment(string? role)
        {
            var normalized = Normalize(role);
            return normalized is Support or SupportLead;
        }

        public static bool IsSafetyDepartment(string? role)
        {
            var normalized = Normalize(role);
            return normalized is Safety or SafetyLead;
        }

        public static bool IsKnownRole(string? role)
        {
            var normalized = Normalize(role);
            return All.Contains(normalized);
        }

        /// <summary>
        /// Whether <paramref name="actorRole"/> may grant/revoke <paramref name="targetRole"/>.
        /// Owner may assign anything; Admin may assign department roles and User only.
        /// </summary>
        public static bool CanAssignRole(string? actorRole, string? targetRole)
        {
            var actor = Normalize(actorRole);
            var target = Normalize(targetRole);

            if (!IsKnownRole(target))
                return false;

            if (actor == Owner)
                return true;

            if (actor != Admin)
                return false;

            // Admin cannot create/remove Owner or peer Admins via grant.
            return target is User or Support or SupportLead or Safety or SafetyLead;
        }
    }
}
