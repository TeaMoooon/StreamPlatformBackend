using BCrypt.Net;
using Microsoft.Extensions.Logging;

namespace StreamPlatformBackend.Services
{
    public interface IPasswordHasherService
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
    }

    public class PasswordHasherService : IPasswordHasherService
    {
        private readonly int _workFactor;
        private readonly ILogger<PasswordHasherService> _logger;

        public PasswordHasherService(ILogger<PasswordHasherService> logger, int workFactor = 12)
        {
            _logger = logger;
            _workFactor = workFactor;

            // Убедимся, что используется правильная версия BCrypt
            BCrypt.Net.BCrypt.GenerateSalt(_workFactor);
        }

        public string HashPassword(string password)
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                    throw new ArgumentException("Password cannot be null or empty");

                // Используем стандартный метод вместо Enhanced
                var salt = BCrypt.Net.BCrypt.GenerateSalt(_workFactor);
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password, salt);

                _logger.LogDebug("Password hashed successfully");
                return hashedPassword;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error hashing password");
                throw new ApplicationException("Error processing password", ex);
            }
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            try
            {
                if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
                    return false;

                // Используем стандартный метод вместо Enhanced
                var isValid = BCrypt.Net.BCrypt.Verify(password, hashedPassword);

                if (!isValid)
                    _logger.LogWarning("Password verification failed");

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying password");
                return false;
            }
        }
    }
}