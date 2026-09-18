using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class LoginHistoryServiceTests
    {
        private static AppDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task RecordSuccessfulLoginAsync_ShouldPersistIpDeviceAndLastAuth()
        {
            await using var db = CreateDb();
            db.Users.Add(new UserModel
            {
                Id = 7,
                Email = "a@b.c",
                Nickname = "alice",
                PasswordHash = "x",
                Role = UserRole.User
            });
            await db.SaveChangesAsync();

            var service = new LoginHistoryService(db, NullLogger<LoginHistoryService>.Instance);
            await service.RecordSuccessfulLoginAsync(7, "203.0.113.10", "Desktop");

            var row = Assert.Single(db.UserLoginHistories);
            Assert.Equal(7, row.UserId);
            Assert.Equal("203.0.113.10", row.IpAddress);
            Assert.Equal("Desktop", row.DeviceType);

            var user = await db.Users.SingleAsync(u => u.Id == 7);
            Assert.NotNull(user.LastAuthDate);
            Assert.Equal(row.LoggedInAt, user.LastAuthDate);
        }

        [Fact]
        public async Task GetForUserAsync_ShouldReturnNewestFirst()
        {
            await using var db = CreateDb();
            db.Users.Add(new UserModel
            {
                Id = 1,
                Email = "a@b.c",
                Nickname = "alice",
                PasswordHash = "x",
                Role = UserRole.User
            });
            db.UserLoginHistories.AddRange(
                new UserLoginHistory { UserId = 1, IpAddress = "1.1.1.1", DeviceType = "Mobile", LoggedInAt = DateTime.UtcNow.AddHours(-2) },
                new UserLoginHistory { UserId = 1, IpAddress = "2.2.2.2", DeviceType = "Desktop", LoggedInAt = DateTime.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();

            var service = new LoginHistoryService(db, NullLogger<LoginHistoryService>.Instance);
            var page = await service.GetForUserAsync(1, skip: 0, take: 10);

            Assert.Equal(2, page.Total);
            Assert.Equal(2, page.Items.Count);
            Assert.Equal("2.2.2.2", page.Items[0].IpAddress);
            Assert.Equal("1.1.1.1", page.Items[1].IpAddress);
            Assert.False(page.HasMore);
        }

        [Fact]
        public async Task GetForUserAsync_ShouldPaginateWithSkipTake()
        {
            await using var db = CreateDb();
            db.Users.Add(new UserModel
            {
                Id = 1,
                Email = "a@b.c",
                Nickname = "alice",
                PasswordHash = "x",
                Role = UserRole.User
            });
            for (var i = 0; i < 7; i++)
            {
                db.UserLoginHistories.Add(new UserLoginHistory
                {
                    UserId = 1,
                    IpAddress = $"1.1.1.{i}",
                    DeviceType = "Chrome · Windows",
                    LoggedInAt = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            await db.SaveChangesAsync();

            var service = new LoginHistoryService(db, NullLogger<LoginHistoryService>.Instance);
            var first = await service.GetForUserAsync(1, skip: 0, take: 5);
            var second = await service.GetForUserAsync(1, skip: 5, take: 5);

            Assert.Equal(7, first.Total);
            Assert.Equal(5, first.Items.Count);
            Assert.True(first.HasMore);
            Assert.Equal(2, second.Items.Count);
            Assert.False(second.HasMore);
            Assert.Equal("1.1.1.5", second.Items[0].IpAddress);
        }
    }

    public class ClientRequestInfoTests
    {
        private static DefaultHttpContext Ctx(string? ua = null, string? xff = null, string? realIp = null)
        {
            var ctx = new DefaultHttpContext();
            if (ua != null) ctx.Request.Headers.UserAgent = ua;
            if (xff != null) ctx.Request.Headers["X-Forwarded-For"] = xff;
            if (realIp != null) ctx.Request.Headers["X-Real-IP"] = realIp;
            return ctx;
        }

        [Theory]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36", "Chrome · Windows")]
        [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
        [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iPad")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0", "Firefox · Windows")]
        [InlineData("", "Неизвестно")]
        public void GetDeviceType_ShouldClassifyUserAgent(string ua, string expected)
        {
            Assert.Equal(expected, ClientRequestInfo.GetDeviceType(Ctx(ua).Request));
        }

        [Fact]
        public void GetIpAddress_ShouldPreferXForwardedForFirstHop()
        {
            var request = Ctx(xff: "203.0.113.50, 10.0.0.1").Request;
            Assert.Equal("203.0.113.50", ClientRequestInfo.GetIpAddress(request));
        }
    }
}
