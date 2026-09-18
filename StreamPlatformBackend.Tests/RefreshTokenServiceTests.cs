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
                    ["Jwt:RefreshTokenDays"] = "60"
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
        public async Task RotateAsync_ReuseOfRotatedToken_ShouldRevokeAllSessions()
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
        public async Task RotateAsync_ReuseAfterExplicitRevoke_ShouldNotKillOtherSessions()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var chrome = await service.IssueAsync(user);
                var edge = await service.IssueAsync(user);

                await service.RevokeAllExceptAsync(user.Id, chrome.RefreshToken);

                // Edge tries to refresh with its explicitly revoked token.
                Assert.Null(await service.RotateAsync(edge.RefreshToken));

                // Chrome must still be alive — unlike reuse-after-rotation, no nuke.
                Assert.Single(await service.GetActiveSessionsAsync(user.Id));
                Assert.NotNull(await service.RotateAsync(chrome.RefreshToken));
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

        [Fact]
        public async Task IssueAsync_ShouldStoreClientMetadataAndFamily()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var client = new SessionClientInfo
                {
                    IpAddress = "203.0.113.10",
                    DeviceLabel = "Chrome · Windows",
                    DeviceCategory = "Desktop"
                };

                var pair = await service.IssueAsync(user, client);
                var stored = Assert.Single(db.RefreshTokens);

                Assert.Equal(pair.SessionId, stored.Id);
                Assert.Equal("203.0.113.10", stored.IpAddress);
                Assert.Equal("Chrome · Windows", stored.DeviceLabel);
                Assert.Equal("Desktop", stored.DeviceCategory);
                Assert.NotEqual(Guid.Empty, stored.SessionFamilyId);
            }
        }

        [Fact]
        public async Task RotateAsync_ShouldPreserveFamilyAndCreatedAt()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var first = await service.IssueAsync(user, new SessionClientInfo
                {
                    IpAddress = "1.1.1.1",
                    DeviceLabel = "Safari · iPhone",
                    DeviceCategory = "Mobile"
                });

                var original = await db.RefreshTokens.SingleAsync();
                var family = original.SessionFamilyId;
                var created = original.CreatedAt;

                await Task.Delay(20);
                var second = await service.RotateAsync(first.RefreshToken);
                Assert.NotNull(second);

                var active = Assert.Single(db.RefreshTokens.Where(t => t.RevokedAt == null));
                Assert.Equal(family, active.SessionFamilyId);
                Assert.Equal(created, active.CreatedAt);
                Assert.Equal("Safari · iPhone", active.DeviceLabel);
                Assert.True(active.LastSeenAt >= created);
            }
        }

        [Fact]
        public async Task RevokeAllExceptAsync_ShouldKeepCurrentSession()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var a = await service.IssueAsync(user);
                var b = await service.IssueAsync(user);

                await service.RevokeAllExceptAsync(user.Id, a.RefreshToken);

                var tokens = db.RefreshTokens.OrderBy(t => t.Id).ToList();
                Assert.Null(tokens[0].RevokedAt);
                Assert.NotNull(tokens[1].RevokedAt);
                Assert.Single(await service.GetActiveSessionsAsync(user.Id));
                Assert.NotNull(await service.RotateAsync(a.RefreshToken));
            }
        }

        [Fact]
        public async Task RevokeById_ShouldMakeSessionFamilyInactive()
        {
            var (service, db, _) = Create();
            await using (db)
            {
                var user = SeedUser(db);
                var pair = await service.IssueAsync(user, new SessionClientInfo
                {
                    IpAddress = "1.2.3.4",
                    DeviceLabel = "Edge · Windows",
                    DeviceCategory = "Desktop"
                });

                var family = (await db.RefreshTokens.SingleAsync()).SessionFamilyId;
                Assert.True(await service.IsSessionFamilyActiveAsync(family));

                Assert.True(await service.RevokeByIdForUserAsync(user.Id, pair.SessionId));
                Assert.False(await service.IsSessionFamilyActiveAsync(family));
            }
        }
    }
}
