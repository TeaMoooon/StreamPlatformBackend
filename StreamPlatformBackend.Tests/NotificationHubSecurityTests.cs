using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class NotificationHubSecurityTests
    {
        private static readonly string[] ForbiddenClientMethods =
        {
            "SendPersonalNotification",
            "NotifyStreamerSubscribers",
            "SendToUser",
            "Broadcast",
            "SendNotification"
        };

        [Fact]
        public void Hub_MustNotExposeClientCallableBroadcastMethods()
        {
            var publicInstanceMethods = typeof(NotificationHub)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var forbidden in ForbiddenClientMethods)
            {
                Assert.False(
                    publicInstanceMethods.Contains(forbidden),
                    $"NotificationHub must not expose client-callable method '{forbidden}'. " +
                    "Outbound notifications belong on INotificationSender / IHubContext only.");
            }

            // Receive-side group membership helpers remain allowed.
            Assert.Contains("SubscribeToStreamer", publicInstanceMethods);
            Assert.Contains("UnsubscribeFromStreamer", publicInstanceMethods);
        }

        [Fact]
        public async Task SubscribeToStreamer_ShouldNotJoinGroupWhenUserDoesNotFollow()
        {
            var userService = new Mock<IUserService>(MockBehavior.Strict);
            userService
                .Setup(s => s.IsSubscribedAsync(10, 99))
                .ReturnsAsync(false);

            var groups = new Mock<IGroupManager>(MockBehavior.Strict);
            var hub = CreateHub(userService.Object, userId: 10, connectionId: "conn-a", groups.Object);

            await hub.SubscribeToStreamer(99);

            groups.Verify(
                g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            userService.VerifyAll();
        }

        [Fact]
        public async Task SubscribeToStreamer_ShouldJoinGroupWhenUserFollowsStreamer()
        {
            var userService = new Mock<IUserService>(MockBehavior.Strict);
            userService
                .Setup(s => s.IsSubscribedAsync(10, 99))
                .ReturnsAsync(true);

            var groups = new Mock<IGroupManager>(MockBehavior.Strict);
            groups
                .Setup(g => g.AddToGroupAsync(
                    "conn-b",
                    NotificationHub.StreamerSubsGroup(99),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hub = CreateHub(userService.Object, userId: 10, connectionId: "conn-b", groups.Object);

            await hub.SubscribeToStreamer(99);

            groups.VerifyAll();
            userService.VerifyAll();
        }

        [Fact]
        public async Task SubscribeToStreamer_ShouldIgnoreInvalidStreamerId()
        {
            var userService = new Mock<IUserService>(MockBehavior.Strict);
            var groups = new Mock<IGroupManager>(MockBehavior.Strict);
            var hub = CreateHub(userService.Object, userId: 10, connectionId: "conn-c", groups.Object);

            await hub.SubscribeToStreamer(0);
            await hub.SubscribeToStreamer(-5);

            groups.Verify(
                g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            userService.Verify(
                s => s.IsSubscribedAsync(It.IsAny<int>(), It.IsAny<int>()),
                Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_ShouldJoinOnlyPersonalAndFollowedStreamerGroups()
        {
            var userService = new Mock<IUserService>(MockBehavior.Strict);
            userService
                .Setup(s => s.GetSubscribedStreamerIdsAsync(7))
                .ReturnsAsync(new List<int> { 3, 5 });

            var groups = new Mock<IGroupManager>(MockBehavior.Strict);
            groups
                .Setup(g => g.AddToGroupAsync("conn-d", NotificationHub.UserGroup(7), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            groups
                .Setup(g => g.AddToGroupAsync("conn-d", NotificationHub.StreamerSubsGroup(3), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            groups
                .Setup(g => g.AddToGroupAsync("conn-d", NotificationHub.StreamerSubsGroup(5), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hub = CreateHub(userService.Object, userId: 7, connectionId: "conn-d", groups.Object);

            await hub.OnConnectedAsync();

            groups.VerifyAll();
            userService.VerifyAll();
        }

        [Fact]
        public async Task UnsubscribeFromStreamer_ShouldRemoveFromGroup()
        {
            var userService = new Mock<IUserService>(MockBehavior.Strict);
            var groups = new Mock<IGroupManager>(MockBehavior.Strict);
            groups
                .Setup(g => g.RemoveFromGroupAsync(
                    "conn-e",
                    NotificationHub.StreamerSubsGroup(42),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hub = CreateHub(userService.Object, userId: 11, connectionId: "conn-e", groups.Object);

            await hub.UnsubscribeFromStreamer(42);

            groups.VerifyAll();
        }

        private static NotificationHub CreateHub(
            IUserService userService,
            int userId,
            string connectionId,
            IGroupManager groups)
        {
            var hub = new NotificationHub(userService, NullLogger<NotificationHub>.Instance);

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            }, authenticationType: "test");

            var context = new Mock<HubCallerContext>();
            context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(identity));
            context.SetupGet(c => c.ConnectionId).Returns(connectionId);

            hub.Context = context.Object;
            hub.Groups = groups;
            hub.Clients = new Mock<IHubCallerClients>(MockBehavior.Strict).Object;

            return hub;
        }
    }
}
