using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.Services.NotificationService;

namespace StreamPlatformBackend.Tests
{
    public class AuthAndCallbackRegressionTests
    {
        [Fact]
        public async Task LoginAsync_ShouldAuthenticateWhenEmailHasSurroundingWhitespace()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;

            await using var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = 1,
                Email = "streamer@example.com",
                Nickname = "streamer",
                Role = UserRole.User,
                PasswordHash = "stored-hash"
            });
            await db.SaveChangesAsync();

            var passwordHasher = new Mock<IPasswordHasherService>(MockBehavior.Strict);
            passwordHasher
                .Setup(p => p.VerifyPassword("correct-password", "stored-hash"))
                .Returns(true);

            var service = new UserService(
                db,
                new ConfigurationBuilder().Build(),
                passwordHasher.Object,
                Mock.Of<INotificationRepository>(),
                Mock.Of<INotificationSender>(),
                NullLogger<UserService>.Instance);

            var user = await service.LoginAsync("  streamer@example.com  ", "correct-password");

            Assert.NotNull(user);
            Assert.Equal(1, user!.Id);
        }

        [Fact]
        public async Task LoginAsync_ShouldAuthenticateWhenNicknameHasSurroundingWhitespace()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;

            await using var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = 7,
                Email = "viewer@example.com",
                Nickname = "streamfan",
                Role = UserRole.User,
                PasswordHash = "stored-hash"
            });
            await db.SaveChangesAsync();

            var passwordHasher = new Mock<IPasswordHasherService>(MockBehavior.Strict);
            passwordHasher
                .Setup(p => p.VerifyPassword("correct-password", "stored-hash"))
                .Returns(true);

            var service = new UserService(
                db,
                new ConfigurationBuilder().Build(),
                passwordHasher.Object,
                Mock.Of<INotificationRepository>(),
                Mock.Of<INotificationSender>(),
                NullLogger<UserService>.Instance);

            var user = await service.LoginAsync("  streamfan  ", "correct-password");

            Assert.NotNull(user);
            Assert.Equal(7, user!.Id);
        }

        [Fact]
        public async Task EndThenRestartWithinReconnectWindow_ShouldNotSelfTerminateWithoutImmediateHeartbeat()
        {
            var databaseName = $"db_{Guid.NewGuid():N}";

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));

            var liveNotifier = new Mock<IStreamLiveNotifier>(MockBehavior.Strict);
            liveNotifier
                .Setup(n => n.PublishStatusAsync(It.IsAny<int>(), It.IsAny<DTO.StreamDTO.StreamInfoDto?>()))
                .Returns(Task.CompletedTask);
            liveNotifier
                .Setup(n => n.ScheduleRepublish(It.IsAny<int>(), It.IsAny<TimeSpan[]>()))
                .Verifiable();

            var catalogCache = new Mock<ICatalogCache>(MockBehavior.Strict);
            catalogCache.SetupGet(c => c.LiveTtl).Returns(TimeSpan.FromSeconds(15));
            catalogCache.SetupGet(c => c.ChannelTtl).Returns(TimeSpan.FromMinutes(5));
            catalogCache
                .Setup(c => c.InvalidateLiveListsAsync())
                .Returns(Task.CompletedTask);
            catalogCache
                .Setup(c => c.InvalidateChannelAsync(It.IsAny<int>(), It.IsAny<string?[]>()))
                .Returns(Task.CompletedTask);

            services.AddSingleton(liveNotifier.Object);
            services.AddSingleton(catalogCache.Object);

            var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Users.Add(new UserModel
            {
                Id = 42,
                Email = "reconnect@example.com",
                Nickname = "reconnecter",
                Role = UserRole.User,
                PasswordHash = "x",
                StreamKey = "live_42_abc123"
            });
            await db.SaveChangesAsync();

            var notificationSender = new Mock<INotificationSender>(MockBehavior.Strict);
            notificationSender
                .Setup(n => n.NotifyStreamerSubscribersAsync(
                    It.IsAny<IEnumerable<UserModel>>(),
                    42,
                    It.IsAny<object>(),
                    NotificationType.StreamStarted))
                .Returns(Task.CompletedTask);

            var platformSanctions = new Mock<IPlatformSanctionService>(MockBehavior.Strict);
            platformSanctions
                .Setup(s => s.BlocksStreamingAsync(42))
                .ReturnsAsync(false);

            var service = new StreamService(
                db,
                Mock.Of<INotificationRepository>(),
                notificationSender.Object,
                NullLogger<StreamService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                liveNotifier.Object,
                platformSanctions.Object,
                catalogCache.Object);

            var started = await service.StartStreamAsync(42, "live_42_abc123");
            Assert.Null(started.EndedAt);

            var endAccepted = await service.EndStreamAsync(42, "live_42_abc123");
            Assert.True(endAccepted);

            var restarted = await service.StartStreamAsync(42, "live_42_abc123");
            Assert.Equal(started.Id, restarted.Id);
            Assert.Null(restarted.EndedAt);

            await Task.Delay(TimeSpan.FromSeconds(31));

            await using var verifyScope = provider.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshedUser = await verifyDb.Users
                .Include(u => u.CurrentStream)
                .SingleAsync(u => u.Id == 42);
            var refreshedStream = await verifyDb.Streams.SingleAsync(s => s.Id == started.Id);

            Assert.True(refreshedUser.IsOnline);
            Assert.NotNull(refreshedUser.CurrentStream);
            Assert.Null(refreshedStream.EndedAt);
        }

        [Fact]
        public async Task StartStream_ShouldRejectSecondPublisher_WhileLiveNotWaitingReconnect()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            await using var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = 7,
                Email = "a@example.com",
                Nickname = "live",
                Role = UserRole.User,
                PasswordHash = "x",
                StreamKey = "live_7_key"
            });
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IStreamLiveNotifier>());
            services.AddSingleton(Mock.Of<ICatalogCache>());
            var provider = services.BuildServiceProvider();

            var sanctions = new Mock<IPlatformSanctionService>(MockBehavior.Strict);
            sanctions.Setup(s => s.BlocksStreamingAsync(7)).ReturnsAsync(false);

            var sender = new Mock<INotificationSender>(MockBehavior.Loose);
            var service = new StreamService(
                db,
                Mock.Of<INotificationRepository>(),
                sender.Object,
                NullLogger<StreamService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IStreamLiveNotifier>(),
                sanctions.Object,
                Mock.Of<ICatalogCache>());

            var first = await service.StartStreamAsync(7, "live_7_key", "203.0.113.10");
            Assert.Equal("203.0.113.10", first.PublisherIp);
            Assert.Equal("203.0.113.10", first.LastPublisherIp);

            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.StartStreamAsync(7, "live_7_key", "198.51.100.20"));
            Assert.Contains("already live", ex.Message, StringComparison.OrdinalIgnoreCase);

            await service.EndStreamAsync(7, "live_7_key");

            // Soft IP anchor: different IP during WaitingReconnect is rejected.
            var hijack = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.StartStreamAsync(7, "live_7_key", "198.51.100.20"));
            Assert.Contains("different IP", hijack.Message, StringComparison.OrdinalIgnoreCase);

            var resumed = await service.StartStreamAsync(7, "live_7_key", "203.0.113.10");
            Assert.Equal(first.Id, resumed.Id);
            Assert.Equal("203.0.113.10", resumed.PublisherIp);
            Assert.Equal("203.0.113.10", resumed.LastPublisherIp);
        }

        [Fact]
        public async Task StartStream_ShouldAllowReconnect_WhenIncomingIpMissing()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            await using var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = 8,
                Email = "b@example.com",
                Nickname = "live2",
                Role = UserRole.User,
                PasswordHash = "x",
                StreamKey = "live_8_key"
            });
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IStreamLiveNotifier>());
            services.AddSingleton(Mock.Of<ICatalogCache>());
            var provider = services.BuildServiceProvider();

            var sanctions = new Mock<IPlatformSanctionService>(MockBehavior.Strict);
            sanctions.Setup(s => s.BlocksStreamingAsync(8)).ReturnsAsync(false);

            var service = new StreamService(
                db,
                Mock.Of<INotificationRepository>(),
                Mock.Of<INotificationSender>(),
                NullLogger<StreamService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IStreamLiveNotifier>(),
                sanctions.Object,
                Mock.Of<ICatalogCache>());

            await service.StartStreamAsync(8, "live_8_key", "203.0.113.10");
            await service.EndStreamAsync(8, "live_8_key");

            // Callback without addr must not hard-fail reconnect.
            var resumed = await service.StartStreamAsync(8, "live_8_key", publisherIp: null);
            Assert.Equal("203.0.113.10", resumed.PublisherIp);
            Assert.False(resumed.WaitingReconnect);
        }

        [Fact]
        public async Task ForceTerminate_ShouldInvokeRtmpDrop()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            await using var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = 9,
                Email = "c@example.com",
                Nickname = "dropme",
                Role = UserRole.User,
                PasswordHash = "x",
                StreamKey = "live_9_key"
            });
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();

            var sanctions = new Mock<IPlatformSanctionService>(MockBehavior.Strict);
            sanctions.Setup(s => s.BlocksStreamingAsync(9)).ReturnsAsync(false);

            var rtmp = new Mock<IRtmpPublisherControl>(MockBehavior.Strict);
            rtmp.Setup(r => r.TryDropPublisherAsync("live_9_key", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var service = new StreamService(
                db,
                Mock.Of<INotificationRepository>(),
                Mock.Of<INotificationSender>(),
                NullLogger<StreamService>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IStreamLiveNotifier>(),
                sanctions.Object,
                Mock.Of<ICatalogCache>(),
                rtmp.Object);

            await service.StartStreamAsync(9, "live_9_key", "203.0.113.50");
            Assert.True(await service.ForceTerminateActiveStreamAsync(9, "test"));
            rtmp.Verify(r => r.TryDropPublisherAsync("live_9_key", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
