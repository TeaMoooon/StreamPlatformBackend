using Microsoft.Extensions.Configuration;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace StreamPlatformBackend.Tests
{
    public class JwtServiceTests
    {
        private readonly IConfiguration _configuration;

        public JwtServiceTests()
        {
            var inMemorySettings = new Dictionary<string, string> {
                {"Jwt:SecretKey", "ThisIsASuperLongSecretKey1234567890"},
                {"Jwt:Issuer", "TestIssuer"},
                {"Jwt:Audience", "TestAudience"}
            };

            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        [Fact]
        public void GenerateToken_ShouldReturnValidToken_WithCorrectClaims()
        {
            // Arrange
            var service = new JwtService(_configuration);
            var user = new UserModel
            {
                Id = 1,
                Email = "test@example.com",
                Nickname = "tester",
                Role = UserRole.User
            };

            // Act
            var tokenString = service.GenerateToken(user);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(tokenString));

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(tokenString);

            Assert.Contains(token.Claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "1");
            Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Email && c.Value == "test@example.com");
            Assert.Contains(token.Claims, c => c.Type == ClaimTypes.Name && c.Value == "tester");
            Assert.Contains(token.Claims, c => c.Type == "Role" && c.Value == "User");
        }

        [Fact]
        public void GenerateToken_NullUser_ShouldThrowArgumentNullException()
        {
            var service = new JwtService(_configuration);
            Assert.Throws<ArgumentNullException>(() => service.GenerateToken(null));
        }

        [Fact]
        public void Constructor_NoSecretKey_ShouldThrowArgumentException()
        {
            var config = new ConfigurationBuilder().Build();
            Assert.Throws<ArgumentException>(() => new JwtService(config));
        }

        [Fact]
        public void GenerateToken_ShouldExpireWithinConfiguredAccessMinutes()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    {"Jwt:SecretKey", "ThisIsASuperLongSecretKey1234567890"},
                    {"Jwt:Issuer", "TestIssuer"},
                    {"Jwt:Audience", "TestAudience"},
                    {"Jwt:AccessTokenMinutes", "15"}
                })
                .Build();

            var service = new JwtService(config);
            Assert.Equal(15 * 60, service.AccessTokenSeconds);

            var tokenString = service.GenerateToken(new UserModel
            {
                Id = 2,
                Email = "a@b.c",
                Nickname = "n",
                Role = UserRole.User
            });

            var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
            var remainingMinutes = (token.ValidTo - DateTime.UtcNow).TotalMinutes;
            Assert.True(remainingMinutes > 14 && remainingMinutes <= 15.1);
        }
    }
}
