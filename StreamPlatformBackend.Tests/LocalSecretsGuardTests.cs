using Microsoft.Extensions.Configuration;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class LocalSecretsGuardTests
    {
        [Fact]
        public void EnsureConfigured_ShouldPassForStrongLocalSecrets()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Port=5432;Database=StreamDB;Username=app;Password=real-db-password",
                    ["Jwt:SecretKey"] = "abcdefghijklmnopqrstuvwxyz0123456789_EXTRA",
                    ["Rtmp:Secret"] = "rtmp-shared-secret-16+"
                })
                .Build();

            LocalSecretsGuard.EnsureConfigured(config);
        }

        [Theory]
        [InlineData("Jwt:SecretKey", "short")]
        [InlineData("Jwt:SecretKey", "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_32PLUS_CHARS")]
        [InlineData("Jwt:SecretKey", "your-super-secret-key-minimum-32-chars-long-here!")]
        [InlineData("Rtmp:Secret", "your-secret-value")]
        [InlineData("Rtmp:Secret", "CHANGE_ME_MATCH_NGINX_NOTIFY_SECRET")]
        [InlineData("ConnectionStrings:DefaultConnection", "Host=localhost;Password=CHANGE_ME")]
        public void EnsureConfigured_ShouldRejectPlaceholdersAndShortSecrets(string key, string value)
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=StreamDB;Username=app;Password=real-db-password",
                ["Jwt:SecretKey"] = "abcdefghijklmnopqrstuvwxyz0123456789_EXTRA",
                ["Rtmp:Secret"] = "rtmp-shared-secret-16+"
            };
            values[key] = value;

            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            var ex = Assert.Throws<InvalidOperationException>(() => LocalSecretsGuard.EnsureConfigured(config));
            Assert.Contains(key, ex.Message);
        }

        [Fact]
        public void IsInsecurePlaceholder_ShouldDetectKnownSamples()
        {
            Assert.True(LocalSecretsGuard.IsInsecurePlaceholder("your-secret-value"));
            Assert.True(LocalSecretsGuard.IsInsecurePlaceholder(""));
            Assert.False(LocalSecretsGuard.IsInsecurePlaceholder("a-real-production-secret-value"));
        }
    }
}
