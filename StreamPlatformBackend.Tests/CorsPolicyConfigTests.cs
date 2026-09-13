using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class CorsPolicyConfigTests
    {
        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Production;
            public string ApplicationName { get; set; } = "Test";
            public string ContentRootPath { get; set; } = "/";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        [Fact]
        public void ResolveAllowedOrigins_UsesConfiguredList_AndDeduplicates()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = "https://192.168.1.167",
                    ["Cors:AllowedOrigins:1"] = " https://192.168.1.167 ",
                    ["Cors:AllowedOrigins:2"] = "http://localhost:3000",
                    ["Cors:AllowedOrigins:3"] = ""
                })
                .Build();

            var origins = CorsPolicyConfig.ResolveAllowedOrigins(
                config,
                new TestHostEnvironment { EnvironmentName = Environments.Production });

            Assert.Equal(new[] { "https://192.168.1.167", "http://localhost:3000" }, origins);
        }

        [Fact]
        public void ResolveAllowedOrigins_ProductionWithoutConfig_Throws()
        {
            var config = new ConfigurationBuilder().Build();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                CorsPolicyConfig.ResolveAllowedOrigins(
                    config,
                    new TestHostEnvironment { EnvironmentName = Environments.Production }));

            Assert.Contains("Cors:AllowedOrigins", ex.Message);
            Assert.Contains("AllowAnyOrigin", ex.Message);
        }

        [Fact]
        public void ResolveAllowedOrigins_DevelopmentWithoutConfig_UsesLocalDefaults()
        {
            var config = new ConfigurationBuilder().Build();

            var origins = CorsPolicyConfig.ResolveAllowedOrigins(
                config,
                new TestHostEnvironment { EnvironmentName = Environments.Development });

            Assert.Contains("https://192.168.1.167", origins);
            Assert.Contains("http://localhost:3000", origins);
            Assert.DoesNotContain("*", origins);
        }

        [Fact]
        public void ApplyFrontendPolicy_NeverUsesAllowAnyOrigin_AndEnablesCredentials()
        {
            var builder = new CorsPolicyBuilder();
            CorsPolicyConfig.ApplyFrontendPolicy(builder, new[] { "https://app.example", "http://localhost:3000" });
            var policy = builder.Build();

            Assert.False(policy.AllowAnyOrigin);
            Assert.True(policy.SupportsCredentials);
            Assert.Contains("https://app.example", policy.Origins);
            Assert.Contains("http://localhost:3000", policy.Origins);
            Assert.DoesNotContain("*", policy.Origins);
        }

        [Fact]
        public void ApplyFrontendPolicy_EmptyOrigins_Throws()
        {
            var builder = new CorsPolicyBuilder();
            Assert.Throws<InvalidOperationException>(() =>
                CorsPolicyConfig.ApplyFrontendPolicy(builder, Array.Empty<string>()));
        }

        [Theory]
        [InlineData("https://192.168.1.167", true)]
        [InlineData("http://localhost:3000", true)]
        [InlineData("https://evil.example", false)]
        [InlineData("https://192.168.1.167.evil.com", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsOriginAllowed_MatchesExactConfiguredOriginsOnly(string? origin, bool expected)
        {
            var allowed = new[] { "https://192.168.1.167", "http://localhost:3000" };
            Assert.Equal(expected, CorsPolicyConfig.IsOriginAllowed(allowed, origin));
        }
    }
}
