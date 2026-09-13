using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class RefreshTokenServiceTests
    {
        private static string HashToken(string rawToken)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static (RefreshTokenService Service, AppDbContext Db, JwtService Jwt) Create()
        {
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SecretKey"] = "ThisIsASuperLongSecretKey1234567890",
                    ["Jwt:Issuer"] = "TestIssuer",
                    ["Jwt:Audience"] = "TestAudience",
                    ["Jwt:AccessTokenMinutes"] = "30",
                    ["Jwt:RefreshTokenDays"] = "30"
                })
                .Build();

            var jwt = new JwtService(config);
            var service = new RefreshTokenService(
                db,
                jwt,
                config,
                NullLogger<RefreshTokenService>.Instance);

            return (service, db, jwt);
        }

        private static UserModel SeedUser(AppDbContext db, int id = 1)
        {
            var user = new UserModel
            {
                Id = id,
                Email = $"user{id}@example.com",
                Nickname = $"user{id}",
                Role = UserRole.User,
                PasswordHash = "hash"
            };
            db.Users.Add(user);
            db.SaveChanges();
            return user;
        }

        [Fact]
        public async Task IssueAsync_ShouldStoreOnlyHashedRefreshToken()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var pair = await service.IssueAsync(user);

                Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
                Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));

                var stored = Assert.Single(db.RefreshTokens);
                Assert.Equal(HashToken(pair.RefreshToken), stored.TokenHash);
                Assert.NotEqual(pair.RefreshToken, stored.TokenHash);
                Assert.Equal(64, stored.TokenHash.Length);
                Assert.Null(stored.RevokedAt);
            }
        }

        [Fact]
        public async Task RotateAsync_ShouldRevokeOldTokenAndIssueNewPair()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var first = await service.IssueAsync(user);

                var second = await service.RotateAsync(first.RefreshToken);

                Assert.NotNull(second);
                Assert.NotEqual(first.RefreshToken, second!.RefreshToken);
                Assert.False(string.IsNullOrWhiteSpace(second.AccessToken));

                var tokens = db.RefreshTokens.OrderBy(t => t.Id).ToList();
                Assert.Equal(2, tokens.Count);
                Assert.NotNull(tokens[0].RevokedAt);
                Assert.Equal(HashToken(second.RefreshToken), tokens[0].ReplacedByTokenHash);
                Assert.Null(tokens[1].RevokedAt);
            }
        }

        [Fact]
        public async Task RotateAsync_ReuseOfRevokedToken_ShouldRevokeAllSessions()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var first = await service.IssueAsync(user);
                var second = await service.RotateAsync(first.RefreshToken);
                Assert.NotNull(second);

                // Attacker presents stolen old refresh token after legitimate rotation.
                var reuse = await service.RotateAsync(first.RefreshToken);
                Assert.Null(reuse);

                Assert.All(db.RefreshTokens, t => Assert.NotNull(t.RevokedAt));

                // Even the latest legitimate refresh is now dead.
                var afterCompromise = await service.RotateAsync(second!.RefreshToken);
                Assert.Null(afterCompromise);
            }
        }

        [Fact]
        public async Task RotateAsync_UnknownOrBlankToken_ShouldReturnNull()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                SeedUser(db);

                Assert.Null(await service.RotateAsync(""));
                Assert.Null(await service.RotateAsync("   "));
                Assert.Null(await service.RotateAsync("not-a-real-token"));
            }
        }

        [Fact]
        public async Task RevokeAsync_ShouldPreventFurtherRotation()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var pair = await service.IssueAsync(user);

                await service.RevokeAsync(pair.RefreshToken);

                Assert.Null(await service.RotateAsync(pair.RefreshToken));
                Assert.NotNull((await db.RefreshTokens.SingleAsync()).RevokedAt);
            }
        }
    }
}
