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
    }

    public interface IRefreshTokenService
    {
        Task<AuthTokenPair> IssueAsync(UserModel user, CancellationToken ct = default);
        Task<AuthTokenPair?> RotateAsync(string rawRefreshToken, CancellationToken ct = default);
        Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default);
        Task RevokeAllForUserAsync(int userId, CancellationToken ct = default);
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
            Math.Max(1, _configuration.GetValue("Jwt:RefreshTokenDays", 30));

        public async Task<AuthTokenPair> IssueAsync(UserModel user, CancellationToken ct = default)
        {
            var raw = GenerateRawToken();
            var entity = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(raw),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays)
            };

            _db.RefreshTokens.Add(entity);
            await _db.SaveChangesAsync(ct);

            return BuildPair(user, raw);
        }

        public async Task<AuthTokenPair?> RotateAsync(string rawRefreshToken, CancellationToken ct = default)
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
                // Possible reuse after theft — revoke all sessions for this user.
                _logger.LogWarning("Refresh token reuse detected for user {UserId}", existing.UserId);
                await RevokeAllForUserAsync(existing.UserId, ct);
                return null;
            }

            if (existing.ExpiresAt <= DateTime.UtcNow)
            {
                existing.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return null;
            }

            var newRaw = GenerateRawToken();
            var newHash = HashToken(newRaw);

            existing.RevokedAt = DateTime.UtcNow;
            existing.ReplacedByTokenHash = newHash;

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = existing.UserId,
                TokenHash = newHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays)
            });

            await _db.SaveChangesAsync(ct);
            return BuildPair(existing.User, newRaw);
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

        private AuthTokenPair BuildPair(UserModel user, string rawRefreshToken)
        {
            var access = _jwtService.GenerateToken(user);
            return new AuthTokenPair
            {
                UserId = user.Id,
                AccessToken = access,
                RefreshToken = rawRefreshToken,
                ExpiresInSeconds = _jwtService.AccessTokenSeconds
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
