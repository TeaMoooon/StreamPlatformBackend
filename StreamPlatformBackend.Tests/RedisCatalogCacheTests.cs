using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Services;
using System.Collections.Concurrent;
using System.Text.Json;

namespace StreamPlatformBackend.Tests
{
    public class RedisCatalogCacheTests
    {
        private readonly Mock<IDatabase> _dbMock;
        private readonly ConcurrentDictionary<string, RedisValue> _store = new();
        private readonly RedisCatalogCache _cache;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public RedisCatalogCacheTests()
        {
            _dbMock = new Mock<IDatabase>();
            var redisMock = new Mock<IConnectionMultiplexer>();
            redisMock
                .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(_dbMock.Object);

            _dbMock
                .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Returns<RedisKey, CommandFlags>((key, _) =>
                {
                    if (_store.TryGetValue(key.ToString() ?? string.Empty, out var value))
                        return Task.FromResult(value);
                    return Task.FromResult(RedisValue.Null);
                });

            _dbMock
                .Setup(db => db.StringSetAsync(
                    It.IsAny<RedisKey>(),
                    It.IsAny<RedisValue>(),
                    It.IsAny<Expiration>(),
                    It.IsAny<ValueCondition>(),
                    It.IsAny<CommandFlags>()))
                .Returns<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>((key, value, _, _, _) =>
                {
                    _store[key.ToString() ?? string.Empty] = value;
                    return Task.FromResult(true);
                });

            _dbMock
                .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .Returns<RedisKey, long, CommandFlags>((key, value, _) =>
                {
                    var name = key.ToString() ?? string.Empty;
                    var current = _store.TryGetValue(name, out var stored) && long.TryParse(stored, out var parsed)
                        ? parsed
                        : 0;
                    var next = current + value;
                    _store[name] = next;
                    return Task.FromResult(next);
                });

            _dbMock
                .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .Returns<RedisKey, CommandFlags>((key, _) =>
                {
                    _store.TryRemove(key.ToString() ?? string.Empty, out RedisValue _);
                    return Task.FromResult(true);
                });

            _dbMock
                .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .Returns<RedisKey[], CommandFlags>((keys, _) =>
                {
                    long removed = 0;
                    foreach (var key in keys)
                    {
                        if (_store.TryRemove(key.ToString() ?? string.Empty, out RedisValue _))
                            removed++;
                    }
                    return Task.FromResult(removed);
                });

            _cache = new RedisCatalogCache(redisMock.Object, NullLogger<RedisCatalogCache>.Instance);
        }

        [Fact]
        public void TagToken_UsesPlaceholderForEmptyAndInvalid()
        {
            Assert.Equal("_", CatalogCacheKeys.TagToken(null));
            Assert.Equal("_", CatalogCacheKeys.TagToken("  "));
            Assert.Equal("dota2", CatalogCacheKeys.TagToken("Dota2"));
            Assert.Equal("_", CatalogCacheKeys.TagToken("hello world"));
        }

        [Fact]
        public void ChannelKey_NormalizesNickname()
        {
            Assert.Equal("catalog:channel:marmok", _cache.ChannelKey(" MarMok "));
        }

        [Fact]
        public void ChannelKey_BlankNickname_UsesPlaceholderInsteadOfEmpty()
        {
            // Adversarial expectation: invalid/blank nicknames must not produce empty Redis keys
            // because that can cause cache collisions.
            Assert.Equal("catalog:channel:_", _cache.ChannelKey("   "));
            Assert.Equal("catalog:channel:_", _cache.ChannelKey(""));
        }

        [Fact]
        public async Task GetLivePageKeyAsync_IncludesGenerationCategoryAndTag()
        {
            _store[CatalogCacheKeys.LiveGeneration] = 7;

            var key = await _cache.GetLivePageKeyAsync(1, 25, 3, "Dota2");

            Assert.Equal(CatalogCacheKeys.LivePage(7, 1, 25, 3, "dota2"), key);
        }

        [Fact]
        public async Task GetOrSetAsync_ReturnsCachedValueWithoutFactory()
        {
            var payload = new CachedLiveStreamsPage
            {
                TotalStreams = 1,
                Streams = new List<OnlineUserListDto>
                {
                    new() { UserId = 9, Nickname = "marmok", StreamName = "live" }
                }
            };
            _store["catalog:live:test"] = JsonSerializer.Serialize(payload, JsonOptions);

            var factoryCalls = 0;
            var result = await _cache.GetOrSetAsync("catalog:live:test", TimeSpan.FromSeconds(15), () =>
            {
                factoryCalls++;
                return Task.FromResult<CachedLiveStreamsPage?>(new CachedLiveStreamsPage());
            });

            Assert.Equal(0, factoryCalls);
            Assert.NotNull(result);
            Assert.Equal(1, result!.TotalStreams);
            Assert.Equal("marmok", result.Streams[0].Nickname);
        }

        [Fact]
        public async Task GetOrSetAsync_WritesOnMiss()
        {
            var result = await _cache.GetOrSetAsync("catalog:channel:tester", TimeSpan.FromMinutes(5), () =>
                Task.FromResult<UserPublicProfileDto?>(new UserPublicProfileDto
                {
                    Id = 4,
                    Nickname = "tester",
                    ProfileDescription = "hi"
                }));

            Assert.NotNull(result);
            Assert.Equal("tester", result!.Nickname);
            Assert.True(_store.ContainsKey("catalog:channel:tester"));
            Assert.Contains("tester", _store["catalog:channel:tester"].ToString());
        }

        [Fact]
        public async Task GetOrSetAsync_DoesNotCacheNull()
        {
            var result = await _cache.GetOrSetAsync<UserPublicProfileDto>("catalog:channel:missing", TimeSpan.FromMinutes(5), () =>
                Task.FromResult<UserPublicProfileDto?>(null));

            Assert.Null(result);
            Assert.False(_store.ContainsKey("catalog:channel:missing"));
        }

        [Fact]
        public async Task GetOrSetAsync_SingleFactoryOnConcurrentMiss()
        {
            var factoryCalls = 0;
            async Task<CachedLiveStreamsPage?> Factory()
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(50);
                return new CachedLiveStreamsPage { TotalStreams = 2 };
            }

            var first = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var second = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var results = await Task.WhenAll(first, second);

            Assert.Equal(1, factoryCalls);
            Assert.Equal(2, results[0]!.TotalStreams);
            Assert.Equal(2, results[1]!.TotalStreams);
            Assert.True(_store.ContainsKey("catalog:live:home"));
        }

        [Fact]
        public async Task GetOrSetAsync_FallsBackToFactoryWhenRedisReadFails()
        {
            _dbMock
                .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var result = await _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), () =>
                Task.FromResult<CachedLiveStreamsPage?>(new CachedLiveStreamsPage { TotalStreams = 3 }));

            Assert.Equal(3, result!.TotalStreams);
        }

        [Fact]
        public async Task GetOrSetAsync_ShouldSingleFlightFactoryEvenWhenRedisReadThrows()
        {
            // Arrange
            _dbMock
                .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

            var factoryCalls = 0;
            async Task<CachedLiveStreamsPage?> Factory()
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(50); // make overlap deterministic
                return new CachedLiveStreamsPage { TotalStreams = 4 };
            }

            // Act
            var t1 = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var t2 = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var t3 = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var t4 = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);
            var t5 = _cache.GetOrSetAsync("catalog:live:home", TimeSpan.FromSeconds(15), Factory);

            var results = await Task.WhenAll(t1, t2, t3, t4, t5);

            // Assert (adversarial expectation: redis failures must not trigger a factory stampede)
            Assert.Equal(1, factoryCalls);
            Assert.All(results, r => Assert.NotNull(r));
            Assert.True(_store.Count == 0 || _store.ContainsKey("catalog:live:home"));
        }

        [Fact]
        public async Task GetOrSetAsync_ShouldRecomputeWhenCachedPayloadIsJsonNull()
        {
            _store["catalog:channel:null-user"] = "null";

            var factoryCalls = 0;
            var result = await _cache.GetOrSetAsync("catalog:channel:null-user", TimeSpan.FromMinutes(5), () =>
            {
                factoryCalls++;
                return Task.FromResult<UserPublicProfileDto?>(new UserPublicProfileDto
                {
                    Id = 17,
                    Nickname = "recovered"
                });
            });

            // Adversarial expectation: JSON null in Redis should be treated as a corrupted cache entry,
            // otherwise the cache can permanently suppress recomputation for a valid object key.
            Assert.Equal(1, factoryCalls);
            Assert.NotNull(result);
            Assert.Equal("recovered", result!.Nickname);
        }

        [Fact]
        public async Task GetOrSetAsync_ShouldIsolateInflightWorkByRequestedType()
        {
            var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var profileFactoryCalls = 0;
            var pageFactoryCalls = 0;

            var profileTask = _cache.GetOrSetAsync("catalog:mixed:key", TimeSpan.FromMinutes(5), async () =>
            {
                Interlocked.Increment(ref profileFactoryCalls);
                await releaseFactory.Task;
                return new UserPublicProfileDto
                {
                    Id = 99,
                    Nickname = "typed-user"
                };
            });

            var pageTask = _cache.GetOrSetAsync("catalog:mixed:key", TimeSpan.FromMinutes(5), () =>
            {
                Interlocked.Increment(ref pageFactoryCalls);
                return Task.FromResult<CachedLiveStreamsPage?>(new CachedLiveStreamsPage
                {
                    TotalStreams = 5
                });
            });

            releaseFactory.SetResult();

            var ex = await Record.ExceptionAsync(() => Task.WhenAll(new Task[] { profileTask, pageTask }));

            // Adversarial expectation: same Redis key requested as different DTO types should not crash
            // the cache's single-flight coordination with invalid casts.
            Assert.Null(ex);
            Assert.Equal(1, profileFactoryCalls);
            Assert.Equal(1, pageFactoryCalls);
        }

        [Fact]
        public async Task InvalidateChannelAsync_ShouldPreventInflightFactoryFromRepopulatingStaleValue()
        {
            const int userId = 12;
            var channelKey = CatalogCacheKeys.Channel("racer");
            var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var getTask = _cache.GetOrSetAsync(channelKey, TimeSpan.FromMinutes(5), async () =>
            {
                factoryEntered.SetResult();
                await releaseFactory.Task;
                return new UserPublicProfileDto
                {
                    Id = userId,
                    Nickname = "racer"
                };
            });

            await factoryEntered.Task;
            await _cache.BindChannelAsync(userId, "racer");
            await _cache.InvalidateChannelAsync(userId, "racer");

            releaseFactory.SetResult();
            var result = await getTask;

            Assert.NotNull(result);
            // Adversarial expectation: once invalidation wins a race, stale data should not be written back
            // by an already-running factory for the same logical channel.
            Assert.False(_store.ContainsKey(channelKey));
        }

        [Fact]
        public async Task InvalidateLiveListsAsync_IncrementsGeneration()
        {
            await _cache.InvalidateLiveListsAsync();
            await _cache.InvalidateLiveListsAsync();

            Assert.Equal("2", _store[CatalogCacheKeys.LiveGeneration].ToString());
            var keyBefore = CatalogCacheKeys.LivePage(1, 1, 25, 0, "_");
            var keyAfter = await _cache.GetLivePageKeyAsync(1, 25, null, null);
            Assert.Equal(CatalogCacheKeys.LivePage(2, 1, 25, 0, "_"), keyAfter);
            Assert.NotEqual(keyBefore, keyAfter);
        }

        [Fact]
        public async Task InvalidateChannelAsync_DeletesBoundAndProvidedNicknames()
        {
            _store[CatalogCacheKeys.ChannelId(12)] = "oldnick";
            _store[CatalogCacheKeys.Channel("oldnick")] = "{}";
            _store[CatalogCacheKeys.Channel("newnick")] = "{}";

            await _cache.InvalidateChannelAsync(12, "NewNick");

            Assert.False(_store.ContainsKey(CatalogCacheKeys.ChannelId(12)));
            Assert.False(_store.ContainsKey(CatalogCacheKeys.Channel("oldnick")));
            Assert.False(_store.ContainsKey(CatalogCacheKeys.Channel("newnick")));
        }

        [Fact]
        public async Task BindChannelAsync_StoresNicknameByUserId()
        {
            await _cache.BindChannelAsync(12, " MarMok ");

            Assert.Equal("marmok", _store[CatalogCacheKeys.ChannelId(12)].ToString());
        }
    }
}
