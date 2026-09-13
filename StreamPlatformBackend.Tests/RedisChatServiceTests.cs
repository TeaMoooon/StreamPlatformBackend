using Xunit;
using Moq;
using StackExchange.Redis;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.DTO.StreamDTO;
using System.Text.Json;
using System.Collections.Generic;
using System.Net;
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
            var json = JsonSerializer.Serialize(message);

            await _service.AddMessageAsync(streamId, message);

            _dbMock.Verify(db =>
                db.ListRightPushAsync(
                    key,
                    json,
                    When.Always,
                    CommandFlags.None),
                Times.Once);

            _dbMock.Verify(db =>
                db.ListTrimAsync(
                    key,
                    -50,
                    -1,
                    CommandFlags.None),
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
                db.KeyDeleteAsync(key, CommandFlags.None),
                Times.Once);

            foreach (var id in userIds)
            {
                _dbMock.Verify(db =>
                    db.SetAddAsync(
                        key,
                        id,
                        CommandFlags.None),
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
            var json = JsonSerializer.Serialize(message);

            await _service.PublishMessageAsync(streamId, message);

            _subscriberMock.Verify(sub =>
                sub.PublishAsync(
                    RedisChannel.Literal(channel),
                    json,
                    CommandFlags.None),
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
                    It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(),
                    CommandFlags.None),
                Times.Once);
        }

        [Fact]
        public async Task CheckSlowModeAsync_ShouldBypassForMods()
        {
            var result = await _service.CheckSlowModeAsync(1, 42, bypassSlowMode: true);

            Assert.Null(result);
        }

        [Fact]
        public async Task CheckSlowModeAsync_ShouldIgnoreFutureTimestampCorruption()
        {
            var streamerId = 15;
            var userId = 88;
            var futureUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

            _dbMock.Setup(db =>
                db.StringGetAsync(
                    It.Is<RedisKey>(k => k.ToString() == $"chat:{streamerId}:settings:slowmode"),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)"30");

            _dbMock.Setup(db =>
                db.StringGetAsync(
                    It.Is<RedisKey>(k => k.ToString() == $"chat:{streamerId}:lastmsg:{userId}"),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)futureUnix.ToString());

            var result = await _service.CheckSlowModeAsync(streamerId, userId, bypassSlowMode: false);

            Assert.Null(result);
        }

        private RedisChatService CreateServiceWithMockedRedis(RedisValue[] listRangeReturn)
        {
            var localDbMock = new Mock<IDatabase>();
            var redisMock = new Mock<IConnectionMultiplexer>();
            var subscriberMock = new Mock<ISubscriber>();

            redisMock
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(localDbMock.Object);

            redisMock
                .Setup(r => r.GetSubscriber(It.IsAny<object>()))
                .Returns(subscriberMock.Object);

            localDbMock
                .Setup(db =>
                    db.ListRangeAsync(
                        It.IsAny<RedisKey>(),
                        It.IsAny<long>(),
                        It.IsAny<long>(),
                        It.IsAny<CommandFlags>()))
                .ReturnsAsync(listRangeReturn);

            return new RedisChatService(redisMock.Object);
        }

        [Fact]
        public async Task UpdateUsernameForUserAsync_ShouldIgnoreCorruptedJsonMessages()
        {
            // Arrange
            const int targetUserId = 42;
            const string newUsername = "new_user";

            // corrupted element at index 0 should be skipped (method must be resilient)
            var corrupted = (RedisValue)"{ not valid json";
            var validMessage = new ChatMessageDto
            {
                Id = "m1",
                UserId = targetUserId,
                Username = "old_user",
                Text = "hi",
                Role = "User",
                Timestamp = DateTime.UtcNow,
                OffsetSeconds = 0,
                IsDeleted = false
            };
            var validJson = (RedisValue)JsonSerializer.Serialize(validMessage);

            var key = (RedisKey)"chat:1:messages";

            _redisMock
                .Setup(r => r.GetEndPoints(false))
                .Returns(new EndPoint[] { new DnsEndPoint("127.0.0.1", 6379) });

            var serverMock = new Mock<IServer>();
            serverMock.SetupGet(s => s.IsConnected).Returns(true);

            async IAsyncEnumerable<RedisKey> Keys()
            {
                yield return key;
                await Task.CompletedTask;
            }

            serverMock
                .Setup(s => s.KeysAsync(
                    It.IsAny<int>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<int>(),
                    It.IsAny<long>(),
                    It.IsAny<int>(),
                    It.IsAny<CommandFlags>()))
                .Returns(Keys());

            _redisMock
                .Setup(r => r.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>()))
                .Returns(serverMock.Object);

            _dbMock
                .Setup(db => db.ListRangeAsync(key, 0, -1, It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue[] { corrupted, validJson });

            var updatedCalls = new List<(long Index, RedisValue Value)>();
            _dbMock
                .Setup(db => db.ListSetByIndexAsync(key, It.IsAny<long>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, long, RedisValue, CommandFlags>((_, idx, value, __) =>
                {
                    updatedCalls.Add((idx, value));
                })
                .Returns(Task.CompletedTask);

            var service = _service;

            // Act
            var ex = await Record.ExceptionAsync(() =>
                service.UpdateUsernameForUserAsync(targetUserId, newUsername));

            // Assert (adversarial expectation: corrupted Redis JSON should not break the update pipeline)
            Assert.Null(ex);
            Assert.Contains(updatedCalls, c =>
                c.Index == 1 && c.Value.ToString().Contains(newUsername));
        }

        [Fact]
        public async Task GetLastMessagesAsync_ShouldSkipMessagesWithNullRequiredFields()
        {
            // Adversarial expectation:
            // Redis payloads can be structurally valid JSON but semantically invalid
            // (e.g. required string fields are null). The chat reader must not return such messages.
            var streamId = 1;
            var key = $"chat:{streamId}:messages";

            // Text is explicitly null, so deserialization succeeds (no JsonException).
            // Current implementation returns the DTO anyway, with Text == null.
            var jsonWithNullText = (RedisValue)
                "{\"id\":\"m1\",\"userId\":1,\"username\":\"u1\",\"text\":null,\"role\":\"User\",\"timestamp\":\"2020-01-01T00:00:00Z\",\"offsetSeconds\":0,\"isDeleted\":false}";

            _dbMock
                .Setup(db => db.ListRangeAsync(
                    key,
                    It.IsAny<long>(),
                    It.IsAny<long>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue[] { jsonWithNullText });

            var result = await _service.GetLastMessagesAsync(streamId);

            Assert.Empty(result);
        }

        [Fact]
        public async Task SetBansAsync_ShouldNotTransientlyUnbanUsersDuringRefresh()
        {
            const int streamerId = 7;
            const int bannedUserId = 42;
            var bansKey = $"chat:{streamerId}:bans";
            var addObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var currentMembers = new HashSet<int> { bannedUserId };

            _dbMock.Setup(db =>
                db.SetMembersAsync(
                    bansKey,
                    It.IsAny<CommandFlags>()))
                .Returns<RedisKey, CommandFlags>((_, _) =>
                {
                    lock (currentMembers)
                    {
                        return Task.FromResult(currentMembers.Select(id => (RedisValue)id).ToArray());
                    }
                });

            _dbMock.Setup(db =>
                db.SetAddAsync(
                    bansKey,
                    It.IsAny<RedisValue>(),
                    It.IsAny<CommandFlags>()))
                .Returns<RedisKey, RedisValue, CommandFlags>(async (_, value, _) =>
                {
                    addObserved.TrySetResult();
                    await allowAdd.Task;

                    lock (currentMembers)
                    {
                        currentMembers.Add((int)value);
                    }

                    return true;
                });

            _dbMock.Setup(db =>
                db.SetContainsAsync(
                    bansKey,
                    It.IsAny<RedisValue>(),
                    It.IsAny<CommandFlags>()))
                .Returns<RedisKey, RedisValue, CommandFlags>((_, value, _) =>
                {
                    lock (currentMembers)
                    {
                        return Task.FromResult(currentMembers.Contains((int)value));
                    }
                });

            var refreshTask = _service.SetBansAsync(streamerId, new[] { bannedUserId });
            await addObserved.Task;

            var duringRefresh = await _service.IsBannedAsync(streamerId, bannedUserId);

            allowAdd.SetResult();
            await refreshTask;

            Assert.True(duringRefresh);
        }

        [Fact]
        public async Task SetBansAsync_ShouldRemoveUsersNoLongerPresent()
        {
            const int streamerId = 8;
            var bansKey = $"chat:{streamerId}:bans";

            _dbMock.Setup(db =>
                db.SetMembersAsync(
                    bansKey,
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue[] { 1, 2 });

            await _service.SetBansAsync(streamerId, new[] { 2 });

            _dbMock.Verify(db =>
                db.SetAddAsync(bansKey, 2, It.IsAny<CommandFlags>()),
                Times.Once);
            _dbMock.Verify(db =>
                db.SetRemoveAsync(bansKey, 1, It.IsAny<CommandFlags>()),
                Times.Once);
            _dbMock.Verify(db =>
                db.SetRemoveAsync(bansKey, 2, It.IsAny<CommandFlags>()),
                Times.Never);
        }
    }
}
