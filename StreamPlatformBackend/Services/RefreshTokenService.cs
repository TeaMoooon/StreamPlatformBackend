using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public sealed class AuthTokenPair
    {
        public int UserId { get; init; }
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public int ExpiresInSeconds { get; init; }
        public long SessionId { get; init; }
    }

    public interface IRefreshTokenService
    {
        Task<AuthTokenPair> IssueAsync(UserModel user, SessionClientInfo? client = null, CancellationToken ct = default);
        Task<AuthTokenPair?> RotateAsync(string rawRefreshToken, SessionClientInfo? client = null, CancellationToken ct = default);
        Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default);
        Task<bool> RevokeByIdForUserAsync(int userId, long sessionId, CancellationToken ct = default);
        Task RevokeAllForUserAsync(int userId, CancellationToken ct = default);
        Task RevokeAllExceptAsync(int userId, string? keepRawRefreshToken, CancellationToken ct = default);
        Task<IReadOnlyList<RefreshToken>> GetActiveSessionsAsync(int userId, CancellationToken ct = default);
        long? FindActiveSessionId(string? rawRefreshToken);
        Task<bool> IsSessionFamilyActiveAsync(Guid sessionFamilyId, CancellationToken ct = default);
    }

    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly AppDbContext _db;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RefreshTokenService> _logger;

        public RefreshTokenService(
            AppDbContext db,
            IJwtService jwtService,
            IConfiguration configuration,
            ILogger<RefreshTokenService> logger)
        {
            _db = db;
            _jwtService = jwtService;
            _configuration = configuration;
            _logger = logger;
        }

        private int RefreshTokenDays =>
            Math.Max(1, _configuration.GetValue("Jwt:RefreshTokenDays", 60));

        public async Task<AuthTokenPair> IssueAsync(UserModel user, SessionClientInfo? client = null, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var raw = GenerateRawToken();
            var entity = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(raw),
                SessionFamilyId = Guid.NewGuid(),
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.AddDays(RefreshTokenDays),
                IpAddress = client?.IpAddress ?? "unknown",
                DeviceLabel = client?.DeviceLabel ?? "Неизвестно",
                DeviceCategory = client?.DeviceCategory ?? "Unknown"
            };

            _db.RefreshTokens.Add(entity);
            await _db.SaveChangesAsync(ct);

            return BuildPair(user, raw, entity.Id, entity.SessionFamilyId);
        }

        public async Task<AuthTokenPair?> RotateAsync(string rawRefreshToken, SessionClientInfo? client = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return null;

            var hash = HashToken(rawRefreshToken.Trim());
            var existing = await _db.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            if (existing == null || existing.User == null)
            {
                _logger.LogWarning("Refresh token not found");
                return null;
            }

            if (existing.RevokedAt != null)
            {
                // Only rotated tokens (replaced by a successor) imply theft on reuse.
                // Explicit logout / "revoke others" leaves ReplacedByTokenHash null — just reject.
                if (!string.IsNullOrEmpty(existing.ReplacedByTokenHash))
                {
                    _logger.LogWarning(
                        "Refresh token reuse after rotation for user {UserId} — revoking all sessions",
                        existing.UserId);
                    await RevokeAllForUserAsync(existing.UserId, ct);
                }
                else
                {
                    _logger.LogInformation(
                        "Refresh token presented after explicit revoke for user {UserId}",
                        existing.UserId);
                }

                return null;
            }

            if (existing.ExpiresAt <= DateTime.UtcNow)
            {
                existing.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return null;
            }

            var now = DateTime.UtcNow;
            var newRaw = GenerateRawToken();
            var newHash = HashToken(newRaw);

            existing.RevokedAt = now;
            existing.ReplacedByTokenHash = newHash;

            var replacement = new RefreshToken
            {
                UserId = existing.UserId,
                TokenHash = newHash,
                SessionFamilyId = existing.SessionFamilyId,
                CreatedAt = existing.CreatedAt,
                LastSeenAt = now,
                ExpiresAt = now.AddDays(RefreshTokenDays),
                IpAddress = client?.IpAddress ?? existing.IpAddress,
                DeviceLabel = client?.DeviceLabel ?? existing.DeviceLabel,
                DeviceCategory = client?.DeviceCategory ?? existing.DeviceCategory
            };

            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync(ct);
            return BuildPair(existing.User, newRaw, replacement.Id, replacement.SessionFamilyId);
        }

        public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return;

            var hash = HashToken(rawRefreshToken.Trim());
            var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (existing == null || existing.RevokedAt != null)
                return;

            existing.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> RevokeByIdForUserAsync(int userId, long sessionId, CancellationToken ct = default)
        {
            var existing = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId, ct);

            if (existing == null || existing.RevokedAt != null)
                return false;

            // Kill the whole rotation family so any sibling leaf cannot linger.
            var familyId = existing.SessionFamilyId;
            var now = DateTime.UtcNow;
            var family = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.SessionFamilyId == familyId && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var token in family)
                token.RevokedAt = now;

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task RevokeAllForUserAsync(int userId, CancellationToken ct = default)
        {
            var active = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(ct);

            if (active.Count == 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var token in active)
                token.RevokedAt = now;

            await _db.SaveChangesAsync(ct);
        }

        public async Task RevokeAllExceptAsync(int userId, string? keepRawRefreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(keepRawRefreshToken))
                throw new ArgumentException("Current refresh token is required to revoke other sessions");

            var keepHash = HashToken(keepRawRefreshToken.Trim());

            var active = await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ToListAsync(ct);

            if (!active.Any(t => t.TokenHash == keepHash))
                throw new InvalidOperationException("Current session refresh token is not active");

            var now = DateTime.UtcNow;
            var changed = false;
            foreach (var token in active)
            {
                if (token.TokenHash == keepHash)
                    continue;
                token.RevokedAt = now;
                changed = true;
            }

            if (changed)
                await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<RefreshToken>> GetActiveSessionsAsync(int userId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            return await _db.RefreshTokens
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
                .OrderByDescending(t => t.LastSeenAt)
                .ToListAsync(ct);
        }

        public long? FindActiveSessionId(string? rawRefreshToken)
        {
            if (string.IsNullOrWhiteSpace(rawRefreshToken))
                return null;

            var hash = HashToken(rawRefreshToken.Trim());
            var now = DateTime.UtcNow;
            return _db.RefreshTokens
                .AsNoTracking()
                .Where(t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > now)
                .Select(t => (long?)t.Id)
                .FirstOrDefault();
        }

        public async Task<bool> IsSessionFamilyActiveAsync(Guid sessionFamilyId, CancellationToken ct = default)
        {
            if (sessionFamilyId == Guid.Empty)
                return false;

            var now = DateTime.UtcNow;
            return await _db.RefreshTokens
                .AsNoTracking()
                .AnyAsync(
                    t => t.SessionFamilyId == sessionFamilyId
                         && t.RevokedAt == null
                         && t.ExpiresAt > now,
                    ct);
        }

        private AuthTokenPair BuildPair(UserModel user, string rawRefreshToken, long sessionId, Guid sessionFamilyId)
        {
            var access = _jwtService.GenerateToken(user, sessionFamilyId);
            return new AuthTokenPair
            {
                UserId = user.Id,
                AccessToken = access,
                RefreshToken = rawRefreshToken,
                ExpiresInSeconds = _jwtService.AccessTokenSeconds,
                SessionId = sessionId
            };
        }

        private static string GenerateRawToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        internal static string HashToken(string rawToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
