using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using StreamPlatformBackend.Services;
using System;

namespace StreamPlatformBackend.Tests
{
    public class PasswordHasherServiceTests
    {
        private readonly PasswordHasherService _service;

        public PasswordHasherServiceTests()
        {
            // Используем "нулевой" логгер для теста
            var logger = NullLogger<PasswordHasherService>.Instance;
            _service = new PasswordHasherService(logger);
        }

        [Fact]
        public void HashPassword_ShouldReturnHashedPassword()
        {
            // Arrange
            var password = "MySecret123!";

            // Act
            var hashed = _service.HashPassword(password);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(hashed));
            Assert.NotEqual(password, hashed); // хэш не равен исходному паролю
        }

        [Fact]
        public void VerifyPassword_ShouldReturnTrue_ForCorrectPassword()
        {
            // Arrange
            var password = "MySecret123!";
            var hashed = _service.HashPassword(password);

            // Act
            var isValid = _service.VerifyPassword(password, hashed);

            // Assert
            Assert.True(isValid);
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForIncorrectPassword()
        {
            // Arrange
            var password = "MySecret123!";
            var hashed = _service.HashPassword(password);

            // Act
            var isValid = _service.VerifyPassword("WrongPassword", hashed);

            // Assert
            Assert.False(isValid);
        }

        [Fact]
        public void HashPassword_ShouldThrowException_ForEmptyPassword()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _service.HashPassword(""));
            Assert.Throws<ArgumentException>(() => _service.HashPassword(null));
        }

        [Fact]
        public void VerifyPassword_ShouldReturnFalse_ForEmptyOrNullInputs()
        {
            // Arrange
            var hashed = _service.HashPassword("Test123");

            // Act & Assert
            Assert.False(_service.VerifyPassword("", hashed));
            Assert.False(_service.VerifyPassword(null, hashed));
            Assert.False(_service.VerifyPassword("Test123", ""));
            Assert.False(_service.VerifyPassword("Test123", null));
        }
    }
}
