using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class StaffUserServiceTests
    {
        private static DbContextOptions<AppDbContext> CreateDbOptions() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        [Fact]
        public async Task SetRoleAsync_ShouldRevokeSessions_OnSuccess()
        {
            await using var db = new AppDbContext(CreateDbOptions());
            db.Users.AddRange(
                new UserModel
                {
                    Id = 1,
                    Email = "owner@example.com",
                    Nickname = "owner",
                    Role = UserRole.Owner,
                    PasswordHash = "x",
                    StreamKey = "live_1_x"
                },
                new UserModel
                {
                    Id = 2,
                    Email = "mod@example.com",
                    Nickname = "mod",
                    Role = UserRole.Safety,
                    PasswordHash = "x",
                    StreamKey = "live_2_x"
                });
            await db.SaveChangesAsync();

            var refresh = new Mock<IRefreshTokenService>(MockBehavior.Strict);
            refresh
                .Setup(r => r.RevokeAllForUserAsync(2, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var audit = new Mock<IStaffAuditService>(MockBehavior.Loose);
            audit.Setup(a => a.WriteAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var service = new StaffUserService(
                db,
                audit.Object,
                refresh.Object,
                NullLogger<StaffUserService>.Instance);

            var (user, error) = await service.SetRoleAsync(1, UserRole.Owner, 2, UserRole.User);
            Assert.Null(error);
            Assert.NotNull(user);
            Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == 2)).Role);
            refresh.Verify(r => r.RevokeAllForUserAsync(2, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetRoleAsync_ShouldRevertRole_WhenRevokeFails()
        {
            await using var db = new AppDbContext(CreateDbOptions());
            db.Users.AddRange(
                new UserModel
                {
                    Id = 1,
                    Email = "owner@example.com",
                    Nickname = "owner",
                    Role = UserRole.Owner,
                    PasswordHash = "x",
                    StreamKey = "live_1_x"
                },
                new UserModel
                {
                    Id = 2,
                    Email = "mod@example.com",
                    Nickname = "mod",
                    Role = UserRole.Safety,
                    PasswordHash = "x",
                    StreamKey = "live_2_x"
                });
            await db.SaveChangesAsync();

            var refresh = new Mock<IRefreshTokenService>(MockBehavior.Strict);
            refresh
                .Setup(r => r.RevokeAllForUserAsync(2, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));

            var audit = new Mock<IStaffAuditService>(MockBehavior.Strict);

            var service = new StaffUserService(
                db,
                audit.Object,
                refresh.Object,
                NullLogger<StaffUserService>.Instance);

            var (user, error) = await service.SetRoleAsync(1, UserRole.Owner, 2, UserRole.User);
            Assert.Null(user);
            Assert.Contains("сессии", error ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(UserRole.Safety, (await db.Users.SingleAsync(u => u.Id == 2)).Role);
            audit.Verify(
                a => a.WriteAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()),
                Times.Never);
        }
    }
}
