using StackExchange.Redis;
using StreamPlatformBackend.DTO.UserDTO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StreamPlatformBackend.Services
{
    public interface ICatalogCache
    {
        TimeSpan LiveTtl { get; }
        TimeSpan ChannelTtl { get; }

        Task<string> GetLivePageKeyAsync(int page, int pageSize, int? categoryId, string? tag);
        string ChannelKey(string nickname);

        Task<T?> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory);
        Task BindChannelAsync(int userId, string nickname);
        Task InvalidateLiveListsAsync();
        Task InvalidateChannelAsync(int userId, params string?[] nicknames);
    }

    public static class CatalogCacheKeys
    {
        public const string LiveGeneration = "catalog:live:gen";

        public static string LivePage(long generation, int page, int pageSize, int categoryId, string tagToken) =>
            $"catalog:live:{generation}:p{page}:s{pageSize}:c{categoryId}:t{tagToken}";

        public static string Channel(string nickname) => $"catalog:channel:{nickname}";

        public static string ChannelId(int userId) => $"catalog:channel:id:{userId}";

        public static string TagToken(string? tag)
        {
            var value = (tag ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length == 0)
                return "_";
            if (value.Length > 64)
                value = value[..64];
            return Regex.IsMatch(value, @"^[a-z0-9_-]+$") ? value : "_";
        }

        public static string NormalizeNickname(string? nickname)
        {
            var value = (nickname ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return "_";

            return value.Length > 50 ? value[..50] : value;
        }
    }

    public class CachedLiveStreamsPage
    {
        public int TotalStreams { get; set; }
        public List<OnlineUserListDto> Streams { get; set; } = new();
    }

    public class RedisCatalogCache : ICatalogCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly IDatabase _db;
        private readonly ILogger<RedisCatalogCache> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        // Single-flight для фабрики: если Redis стабильно падает, все конкурирующие запросы
        // должны разделять один вычисленный результат, а не дёргать factory N раз.
        // Ключ учитывает и Redis-key, и запрашиваемый DTO-тип, чтобы избежать type confusion.
        private readonly ConcurrentDictionary<string, Task<object?>> _inflight = new();
        // Локальная версионность защищает от репопуляции устаревшего значения после invalidation race.
        private readonly ConcurrentDictionary<string, long> _keyVersions = new();

        public RedisCatalogCache(IConnectionMultiplexer redis, ILogger<RedisCatalogCache> logger)
        {
            _db = redis.GetDatabase();
            _logger = logger;
        }

        public TimeSpan LiveTtl { get; } = TimeSpan.FromSeconds(15);
        public TimeSpan ChannelTtl { get; } = TimeSpan.FromMinutes(5);

        public async Task<string> GetLivePageKeyAsync(int page, int pageSize, int? categoryId, string? tag)
        {
            var generation = await GetLiveGenerationAsync();
            var cat = categoryId is > 0 ? categoryId.Value : 0;
            return CatalogCacheKeys.LivePage(generation, page, pageSize, cat, CatalogCacheKeys.TagToken(tag));
        }

        public string ChannelKey(string nickname) =>
            CatalogCacheKeys.Channel(CatalogCacheKeys.NormalizeNickname(nickname));

        public async Task<T?> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory)
        {
            var typedInflightKey = $"{typeof(T).FullName}|{key}";
            var versionAtStart = GetKeyVersion(key);

            // Fast-path: если Redis отвечает — возвращаем кэш.
            try
            {
                var cached = await _db.StringGetAsync(key);
                if (cached.HasValue)
                {
                    var cachedValue = JsonSerializer.Deserialize<T>(cached!, JsonOptions);
                    if (IsUsableCachedValue(cachedValue))
                        return cachedValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog cache read failed for {Key}", key);
                // Продолжаем вычисление через single-flight.
            }

            // Single-flight: только один вычислитель дергает factory на один ключ.
            var task = _inflight.GetOrAdd(typedInflightKey, _ =>
            {
                var t = ComputeAndPopulateAsync(key, ttl, versionAtStart, factory);
                t.ContinueWith(_ => { _inflight.TryRemove(typedInflightKey, out var _ignored); }, TaskScheduler.Default);
                return t;
            });

            var boxed = await task.ConfigureAwait(false);
            return (T?)boxed;
        }

        private async Task<object?> ComputeAndPopulateAsync<T>(string key, TimeSpan ttl, long versionAtStart, Func<Task<T?>> factory)
        {
            // Re-check внутри “вычисления”, чтобы если другой поток успел записать кэш,
            // мы могли использовать готовое значение.
            try
            {
                var cached = await _db.StringGetAsync(key);
                if (cached.HasValue)
                {
                    var cachedValue = JsonSerializer.Deserialize<T>(cached!, JsonOptions);
                    if (IsUsableCachedValue(cachedValue))
                        return cachedValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog cache re-read failed for {Key}", key);
            }

            var value = await factory();
            if (value is null)
                return null;

            try
            {
                // Если ключ успели инвалидировать, вычисленное значение уже устарело:
                // отдаём его текущему caller, но назад в кэш не записываем.
                if (GetKeyVersion(key) == versionAtStart)
                {
                    var json = JsonSerializer.Serialize(value, JsonOptions);
                    await _db.StringSetAsync(key, json, ttl);
                    if (GetKeyVersion(key) != versionAtStart)
                    {
                        await _db.KeyDeleteAsync(key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog cache write failed for {Key}", key);
            }

            return value;
        }

        public async Task BindChannelAsync(int userId, string nickname)
        {
            var normalized = CatalogCacheKeys.NormalizeNickname(nickname);
            if (userId <= 0 || string.IsNullOrEmpty(normalized))
                return;

            try
            {
                await _db.StringSetAsync(CatalogCacheKeys.ChannelId(userId), normalized, ChannelTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to bind channel cache id {UserId}", userId);
            }
        }

        public async Task InvalidateLiveListsAsync()
        {
            try
            {
                await _db.StringIncrementAsync(CatalogCacheKeys.LiveGeneration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate live catalog cache");
            }
        }

        public async Task InvalidateChannelAsync(int userId, params string?[] nicknames)
        {
            try
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var nickname in nicknames)
                {
                    var normalized = CatalogCacheKeys.NormalizeNickname(nickname);
                    if (!string.IsNullOrEmpty(normalized))
                        names.Add(normalized);
                }

                if (userId > 0)
                {
                    var bound = await _db.StringGetAsync(CatalogCacheKeys.ChannelId(userId));
                    if (bound.HasValue)
                    {
                        var normalized = CatalogCacheKeys.NormalizeNickname(bound.ToString());
                        if (!string.IsNullOrEmpty(normalized))
                            names.Add(normalized);
                    }

                    await _db.KeyDeleteAsync(CatalogCacheKeys.ChannelId(userId));
                }

                if (names.Count == 0)
                    return;

                var keys = names.Select(name => (RedisKey)CatalogCacheKeys.Channel(name)).ToArray();
                foreach (var name in names)
                    MarkKeyInvalidated(CatalogCacheKeys.Channel(name));
                await _db.KeyDeleteAsync(keys);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate channel cache for {UserId}", userId);
            }
        }

        private async Task<long> GetLiveGenerationAsync()
        {
            try
            {
                var value = await _db.StringGetAsync(CatalogCacheKeys.LiveGeneration);
                if (value.HasValue && long.TryParse(value, out var generation))
                    return generation;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read live catalog generation");
            }

            return 0;
        }

        private long GetKeyVersion(string key) =>
            _keyVersions.TryGetValue(key, out var version) ? version : 0;

        private void MarkKeyInvalidated(string key) =>
            _keyVersions.AddOrUpdate(key, 1, (_, version) => version + 1);

        private static bool IsUsableCachedValue<T>(T? value)
        {
            if (value is null)
                return false;

            var type = typeof(T);
            if (type == typeof(string))
                return !string.IsNullOrWhiteSpace(value as string);

            if (type.IsValueType)
                return true;

            var properties = type
                .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(p => p.CanRead)
                .ToArray();
            if (properties.Length == 0)
                return true;

            foreach (var property in properties)
            {
                if (property.Name is "EqualityContract")
                    continue;

                var propertyValue = property.GetValue(value);
                if (propertyValue is null)
                    continue;

                if (property.PropertyType == typeof(string))
                {
                    if (!string.IsNullOrWhiteSpace(propertyValue as string))
                        return true;
                    continue;
                }

                if (property.PropertyType.IsValueType)
                {
                    if (!propertyValue.Equals(Activator.CreateInstance(property.PropertyType)!))
                        return true;
                    continue;
                }

                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    var enumerator = ((System.Collections.IEnumerable)propertyValue).GetEnumerator();
                    if (enumerator.MoveNext())
                        return true;
                    continue;
                }

                return true;
            }

            return false;
        }
    }
}
