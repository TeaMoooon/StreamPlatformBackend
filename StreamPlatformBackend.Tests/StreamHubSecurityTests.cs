using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class StreamHubSecurityTests
    {
        [Fact]
        public async Task SendChatMessage_ShouldRejectAnonymousCaller()
        {
            var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
            var caller = new Mock<ISingleClientProxy>(MockBehavior.Strict);
            caller
                .Setup(c => c.SendCoreAsync(
                    "Error",
                    It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "ChatUnauthorized")),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.SetupGet(c => c.Caller).Returns(caller.Object);

            var hub = CreateAnonymousHub(clients.Object);

            await hub.SendChatMessage("hello");

            caller.VerifyAll();
        }

        [Fact]
        public async Task BanChatUser_ShouldRejectAnonymousCaller()
        {
            var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
            var caller = new Mock<ISingleClientProxy>(MockBehavior.Strict);
            caller
                .Setup(c => c.SendCoreAsync(
                    "Error",
                    It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "ChatUnauthorized")),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.SetupGet(c => c.Caller).Returns(caller.Object);

            var hub = CreateAnonymousHub(clients.Object);

            await hub.BanChatUser(42);

            caller.VerifyAll();
        }

        [Fact]
        public async Task DeleteChatMessage_ShouldRejectAnonymousCaller()
        {
            var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
            var caller = new Mock<ISingleClientProxy>(MockBehavior.Strict);
            caller
                .Setup(c => c.SendCoreAsync(
                    "Error",
                    It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "ChatUnauthorized")),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.SetupGet(c => c.Caller).Returns(caller.Object);

            var hub = CreateAnonymousHub(clients.Object);

            await hub.DeleteChatMessage("msg-1");

            caller.VerifyAll();
        }

        private static StreamHub CreateAnonymousHub(IHubCallerClients clients)
        {
            var hub = new StreamHub(
                Mock.Of<IStreamService>(MockBehavior.Strict),
                Mock.Of<IUserService>(MockBehavior.Strict),
                Mock.Of<IRedisChatService>(MockBehavior.Strict),
                Mock.Of<IStreamChatBanService>(MockBehavior.Strict),
                Mock.Of<IStreamChatHistoryService>(MockBehavior.Strict),
                Mock.Of<IStreamChatModerationLogService>(MockBehavior.Strict),
                Mock.Of<IPlatformSanctionService>(MockBehavior.Strict),
                NullLogger<StreamHub>.Instance);

            var context = new Mock<HubCallerContext>();
            // Unauthenticated principal
            context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));
            context.SetupGet(c => c.ConnectionId).Returns("anon-conn");

            hub.Context = context.Object;
            hub.Clients = clients;
            hub.Groups = new Mock<IGroupManager>(MockBehavior.Strict).Object;

            return hub;
        }
    }
}
