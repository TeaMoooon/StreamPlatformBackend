using StreamPlatformBackend.Models.Enums;

namespace StreamPlatformBackend.Tests
{
    public class UserRolePolicyTests
    {
        [Theory]
        [InlineData(UserRole.Support, true)]
        [InlineData(UserRole.Moderator, true)]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.SuperAdmin, true)]
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
        [InlineData(UserRole.Moderator, true)]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.SuperAdmin, true)]
        [InlineData(UserRole.Support, false)]
        [InlineData(UserRole.User, false)]
        public void CanModeratePlatform_ShouldExcludeSupport(string role, bool expected)
        {
            Assert.Equal(expected, UserRole.CanModeratePlatform(role));
        }

        [Theory]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.SuperAdmin, true)]
        [InlineData(UserRole.Moderator, false)]
        [InlineData(UserRole.Support, false)]
        [InlineData(UserRole.User, false)]
        public void CanManageStaffRoles_ShouldBeAdminOnly(string role, bool expected)
        {
            Assert.Equal(expected, UserRole.CanManageStaffRoles(role));
        }

        [Fact]
        public void StaffRoleArrays_ShouldAlignWithPolicyHelpers()
        {
            Assert.All(UserRole.StaffRoles, role => Assert.True(UserRole.IsStaff(role)));
            Assert.All(UserRole.PlatformModeratorRoles, role => Assert.True(UserRole.CanModeratePlatform(role)));
            Assert.All(UserRole.AdminRoles, role => Assert.True(UserRole.CanManageStaffRoles(role)));
            Assert.DoesNotContain(UserRole.Support, UserRole.PlatformModeratorRoles);
            Assert.DoesNotContain(UserRole.User, UserRole.StaffRoles);
        }
    }
}
