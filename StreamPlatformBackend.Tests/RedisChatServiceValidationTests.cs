using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System;
using System.Threading.Tasks;
using Xunit;

namespace StreamPlatformBackend.Tests
{
    public class RedisChatServiceValidationTests
    {
        [Fact]
        public async Task AddMessageAsync_ShouldRejectEmptyOrWhitespaceText()
        {
            // Arrange
            var dbMock = new Mock<IDatabase>();
            var subscriberMock = new Mock<ISubscriber>();
            var redisMock = new Mock<IConnectionMultiplexer>();

            redisMock
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(dbMock.Object);

            redisMock
                .Setup(r => r.GetSubscriber(It.IsAny<object>()))
                .Returns(subscriberMock.Object);

            var service = new RedisChatService(redisMock.Object);

            var message = new ChatMessageDto
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = 42,
                Username = "test_user",
                Text = "   ",
                Role = "User",
                Timestamp = DateTime.UtcNow,
                OffsetSeconds = 0,
                IsDeleted = false
            };

            // Act + Assert (adversarial expectation: storage layer should be defensive)
            await Assert.ThrowsAsync<ArgumentException>(() => service.AddMessageAsync(1, message));
        }
    }
}

