using Xunit;
using Moq;
using StackExchange.Redis;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.DTO.StreamDTO;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace StreamPlatformBackend.Tests
{
    public class RedisChatServiceTests
    {
        private readonly Mock<IDatabase> _dbMock;
        private readonly Mock<ISubscriber> _subscriberMock;
        private readonly Mock<IConnectionMultiplexer> _redisMock;
        private readonly RedisChatService _service;

        public RedisChatServiceTests()
        {
            _dbMock = new Mock<IDatabase>();
            _subscriberMock = new Mock<ISubscriber>();
            _redisMock = new Mock<IConnectionMultiplexer>();

            _redisMock
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);

            _redisMock
                .Setup(r => r.GetSubscriber(It.IsAny<object>()))
                .Returns(_subscriberMock.Object);

            _service = new RedisChatService(_redisMock.Object);
        }

        [Fact]
        public async Task AddMessageAsync_ShouldPushAndTrim()
        {
            var streamId = 1;

            var message = new ChatMessageDto
            {
                UserId = 42,
                Username = "test_user",
                Text = "Hello",
                Role = "User",
                Timestamp = DateTime.UtcNow,
                OffsetSeconds = 0
            };

            var key = $"chat:{streamId}:messages";

            await _service.AddMessageAsync(streamId, message);

            _dbMock.Verify(db =>
                db.ListRightPushAsync(
                    key,
                    JsonSerializer.Serialize(message)),
                Times.Once);

            _dbMock.Verify(db =>
                db.ListTrimAsync(
                    key,
                    -50,
                    -1),
                Times.Once);
        }

        [Fact]
        public async Task GetLastMessagesAsync_ShouldDeserializeMessages()
        {
            var streamId = 1;

            var redisValues = new RedisValue[]
            {
                JsonSerializer.Serialize(new ChatMessageDto { UserId = 1, Text = "Hi" }),
                JsonSerializer.Serialize(new ChatMessageDto { UserId = 2, Text = "Hello" })
            };

            _dbMock.Setup(db =>
                db.ListRangeAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(redisValues);

            var result = await _service.GetLastMessagesAsync(streamId);

            Assert.Equal(2, result.Count);
            Assert.Equal("Hi", result[0].Text);
            Assert.Equal("Hello", result[1].Text);
        }

        [Fact]
        public async Task SetModeratorsAsync_ShouldDeleteAndAdd()
        {
            var streamerId = 1;
            var userIds = new[] { 10, 20 };
            var key = $"stream:{streamerId}:moderators";

            await _service.SetModeratorsAsync(streamerId, userIds);

            _dbMock.Verify(db =>
                db.KeyDeleteAsync(key),
                Times.Once);

            foreach (var id in userIds)
            {
                _dbMock.Verify(db =>
                    db.SetAddAsync(
                        key,
                        id),
                    Times.Once);
            }
        }

        [Fact]
        public async Task IsModeratorAsync_ShouldReturnTrue()
        {
            _dbMock.Setup(db =>
                db.SetContainsAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var result = await _service.IsModeratorAsync(1, 42);

            Assert.True(result);
        }

        [Fact]
        public async Task PublishMessageAsync_ShouldPublishMessage()
        {
            var streamId = 1;

            var message = new ChatMessageDto
            {
                UserId = 42,
                Text = "Hi!"
            };

            var channel = $"chat_channel:{streamId}";

            await _service.PublishMessageAsync(streamId, message);

            _subscriberMock.Verify(sub =>
                sub.PublishAsync(
                    channel,
                    JsonSerializer.Serialize(message)),
                Times.Once);
        }

        [Fact]
        public async Task GetSlowModeSecondsAsync_ShouldReturnZeroWhenMissing()
        {
            _dbMock.Setup(db =>
                db.StringGetAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains("slowmode")),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var result = await _service.GetSlowModeSecondsAsync(1);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task SetSlowModeSecondsAsync_ShouldStoreValue()
        {
            await _service.SetSlowModeSecondsAsync(1, 30);

            _dbMock.Verify(db =>
                db.StringSetAsync(
                    It.Is<RedisKey>(k => k.ToString().Contains("slowmode")),
                    (RedisValue)30,
                    null,
                    When.Always,
                    CommandFlags.None),
                Times.Once);
        }

        [Fact]
        public async Task CheckSlowModeAsync_ShouldBypassForMods()
        {
            var result = await _service.CheckSlowModeAsync(1, 42, bypassSlowMode: true);

            Assert.Null(result);
        }
    }
}
