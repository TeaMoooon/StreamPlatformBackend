using StreamPlatformBackend.Models.Enums;

namespace StreamPlatformBackend.Tests
{
    public class UserRolePolicyTests
    {
        [Theory]
        [InlineData(UserRole.Support, true)]
        [InlineData(UserRole.SupportLead, true)]
        [InlineData(UserRole.Safety, true)]
        [InlineData(UserRole.SafetyLead, true)]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.Owner, true)]
        [InlineData(UserRole.LegacyModerator, true)]
        [InlineData(UserRole.LegacySuperAdmin, true)]
        [InlineData(UserRole.User, false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("Hacker", false)]
        public void IsStaff_ShouldMatchStaffOnlyPolicy(string? role, bool expected)
        {
            Assert.Equal(expected, UserRole.IsStaff(role));
            Assert.Equal(expected, UserRole.CanAccessStaffPanel(role));
            Assert.Equal(expected, UserRole.CanManageTickets(role));
        }

        [Theory]
        [InlineData(UserRole.Safety, true)]
        [InlineData(UserRole.SafetyLead, true)]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.Owner, true)]
        [InlineData(UserRole.LegacyModerator, true)]
        [InlineData(UserRole.Support, false)]
        [InlineData(UserRole.SupportLead, false)]
        [InlineData(UserRole.User, false)]
        public void CanModeratePlatform_ShouldBeSafetyDepartmentAndAbove(string role, bool expected)
        {
            Assert.Equal(expected, UserRole.CanModeratePlatform(role));
        }

        [Theory]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.Owner, true)]
        [InlineData(UserRole.LegacySuperAdmin, true)]
        [InlineData(UserRole.Safety, false)]
        [InlineData(UserRole.Support, false)]
        [InlineData(UserRole.User, false)]
        public void CanManageStaffRoles_ShouldBeAdminOnly(string role, bool expected)
        {
            Assert.Equal(expected, UserRole.CanManageStaffRoles(role));
        }

        [Theory]
        [InlineData(UserRole.LegacyModerator, UserRole.Safety)]
        [InlineData(UserRole.LegacySuperAdmin, UserRole.Owner)]
        [InlineData(UserRole.Safety, UserRole.Safety)]
        [InlineData(null, UserRole.User)]
        public void Normalize_ShouldMapLegacyRoles(string? input, string expected)
        {
            Assert.Equal(expected, UserRole.Normalize(input));
        }

        [Theory]
        [InlineData(UserRole.Owner, UserRole.Admin, true)]
        [InlineData(UserRole.Owner, UserRole.Owner, true)]
        [InlineData(UserRole.Admin, UserRole.Safety, true)]
        [InlineData(UserRole.Admin, UserRole.SupportLead, true)]
        [InlineData(UserRole.Admin, UserRole.Admin, false)]
        [InlineData(UserRole.Admin, UserRole.Owner, false)]
        [InlineData(UserRole.Safety, UserRole.Support, false)]
        public void CanAssignRole_ShouldRespectHierarchy(string actor, string target, bool expected)
        {
            Assert.Equal(expected, UserRole.CanAssignRole(actor, target));
        }

        [Fact]
        public void StaffRoleArrays_ShouldAlignWithPolicyHelpers()
        {
            Assert.True(UserRole.IsStaff(UserRole.Support));
            Assert.True(UserRole.IsStaff(UserRole.Safety));
            Assert.True(UserRole.CanModeratePlatform(UserRole.Safety));
            Assert.True(UserRole.CanManageStaffRoles(UserRole.Admin));
            Assert.False(UserRole.CanModeratePlatform(UserRole.Support));
            Assert.False(UserRole.IsStaff(UserRole.User));
            Assert.True(UserRole.IsSupportDepartment(UserRole.SupportLead));
            Assert.True(UserRole.IsSafetyDepartment(UserRole.SafetyLead));
            Assert.True(UserRole.Rank(UserRole.Owner) > UserRole.Rank(UserRole.Admin));
            Assert.True(UserRole.Rank(UserRole.SafetyLead) > UserRole.Rank(UserRole.Safety));
        }
    }
}
