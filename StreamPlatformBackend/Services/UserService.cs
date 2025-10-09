using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public interface IUserService
    {
        Task<UserModel> CreateUserAsync(UserCreateDto userCreateDto);
        Task<bool> EmailExistsAsync(string email);
        Task<bool> NicknameExistsAsync(string nickname);
        Task<bool> ValidateUserCredentialsAsync(string email, string password);
        Task<UserModel?> GetUserByEmailAsync(string email);
        Task<UserModel?> GetUserByIdAsync(int id);
        Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto);
    }

    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ILogger<UserService> _logger;

        public UserService(AppDbContext context, IPasswordHasherService passwordHasher, ILogger<UserService> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == email);
        }

        public async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Nickname == nickname);
        }

        public async Task<UserModel?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<UserModel?> GetUserByIdAsync(int id)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<UserModel> CreateUserAsync(UserCreateDto userCreateDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Начало создания пользователя: {Email}", userCreateDto.Email);

                // Проверяем, существует ли email
                if (await EmailExistsAsync(userCreateDto.Email))
                {
                    _logger.LogWarning("Попытка создания пользователя с существующим email: {Email}", userCreateDto.Email);
                    throw new ArgumentException("Email уже используется");
                }

                // Проверяем, существует ли nickname
                if (await NicknameExistsAsync(userCreateDto.Nickname))
                {
                    _logger.LogWarning("Попытка создания пользователя с существующим nickname: {Nickname}", userCreateDto.Nickname);
                    throw new ArgumentException("Никнейм уже используется");
                }

                // Хешируем пароль
                var passwordHash = _passwordHasher.HashPassword(userCreateDto.Password);

                var user = new UserModel
                {
                    Email = userCreateDto.Email,
                    Nickname = userCreateDto.Nickname,
                    PasswordHash = passwordHash,

                    StreamKey = GenerateStreamKey(),
                    StreamServerUrl = "rtmp://your-server.com/live"
                };

                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Пользователь успешно создан: {Email} (ID: {UserId})",
                    userCreateDto.Email, user.Id);

                return user;
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning("Ошибка валидации при создании пользователя: {Message}", ex.Message);
                throw;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Ошибка базы данных при создании пользователя {Email}",
                    userCreateDto.Email);
                throw new ApplicationException("Ошибка при сохранении пользователя в базу данных");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Неожиданная ошибка при создании пользователя {Email}",
                    userCreateDto.Email);
                throw new ApplicationException("Произошла внутренняя ошибка при создании пользователя");
            }
        }

        private static string GenerateStreamKey()
        {
            return $"sk_{Guid.NewGuid():N}";
        }
        //Добавить сброс ключа 

        public async Task<bool> ValidateUserCredentialsAsync(string email, string password)
        {
            try
            {
                _logger.LogDebug("Проверка учетных данных для: {Email}", email);

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    // Задержка для предотвращения timing attacks
                    await Task.Delay(2000);
                    _logger.LogWarning("Попытка входа с несуществующим email: {Email}", email);
                    return false;
                }

                var isValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);

                if (isValid)
                {
                    _logger.LogInformation("Успешная аутентификация пользователя: {Email}", email);

                    user.LastAuthDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    _logger.LogWarning("Неудачная попытка входа: {Email}", email);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке учетных данных для {Email}", email);
                return false;
            }
        }

        /*public async Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    throw new ArgumentException("Пользователь не найден");
                }

                // Проверяем, не занят ли новый email другим пользователем
                if (user.Email != userUpdateDataDto.Email &&
                    await EmailExistsAsync(userUpdateDataDto.Email))
                {
                    throw new ArgumentException("Email уже используется");
                }

                // Проверяем, не занят ли новый nickname другим пользователем
                if (user.Nickname != userUpdateDataDto.Nickname &&
                    await NicknameExistsAsync(userUpdateDataDto.Nickname))
                {
                    throw new ArgumentException("Никнейм уже используется");
                }

                user.Email = userUpdateDataDto.Email;
                user.Nickname = userUpdateDataDto.Nickname;
                user.ProfileDescription = userUpdateDataDto.ProfileDescription;
                user.ProfileImage = userUpdateDataDto.ProfileImage;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Профиль пользователя {UserId} обновлен", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя {UserId}", userId);
                throw;
            }
        }*/

        public async Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    throw new ArgumentException("Пользователь не найден");
                }

                bool hasChanges = false;

                // Проверяем и обновляем Email, если указан
                if (!string.IsNullOrEmpty(userUpdateDataDto.Email))
                {
                    if (user.Email != userUpdateDataDto.Email)
                    {
                        if (await EmailExistsAsync(userUpdateDataDto.Email))
                        {
                            throw new ArgumentException("Email уже используется");
                        }
                        user.Email = userUpdateDataDto.Email;
                        hasChanges = true;
                        _logger.LogDebug("Email обновлен для пользователя {UserId}", userId);
                    }
                }

                // Проверяем и обновляем Nickname, если указан
                if (!string.IsNullOrEmpty(userUpdateDataDto.Nickname))
                {
                    if (user.Nickname != userUpdateDataDto.Nickname)
                    {
                        if (await NicknameExistsAsync(userUpdateDataDto.Nickname))
                        {
                            throw new ArgumentException("Никнейм уже используется");
                        }
                        user.Nickname = userUpdateDataDto.Nickname;
                        hasChanges = true;
                        _logger.LogDebug("Nickname обновлен для пользователя {UserId}", userId);
                    }
                }

                // Обновляем ProfileDescription, если указан (без проверки уникальности)
                if (userUpdateDataDto.ProfileDescription != null)
                {
                    if (user.ProfileDescription != userUpdateDataDto.ProfileDescription)
                    {
                        user.ProfileDescription = userUpdateDataDto.ProfileDescription;
                        hasChanges = true;
                        _logger.LogDebug("ProfileDescription обновлен для пользователя {UserId}", userId);
                    }
                }

                // Обновляем ProfileImage, если указан (без проверки уникальности)
                if (userUpdateDataDto.ProfileImage != null)
                {
                    if (user.ProfileImage != userUpdateDataDto.ProfileImage)
                    {
                        user.ProfileImage = userUpdateDataDto.ProfileImage;
                        hasChanges = true;
                        _logger.LogDebug("ProfileImage обновлен для пользователя {UserId}", userId);
                    }
                }

                // Сохраняем изменения только если они есть
                if (hasChanges)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Профиль пользователя {UserId} успешно обновлен", userId);
                }
                else
                {
                    _logger.LogInformation("Профиль пользователя {UserId} не требует обновления - нет изменений", userId);
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Ошибка валидации при обновлении профиля пользователя {UserId}: {Message}",
                    userId, ex.Message);
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Ошибка базы данных при обновлении профиля пользователя {UserId}", userId);
                throw new ApplicationException("Ошибка при сохранении изменений в базу данных");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при обновлении профиля пользователя {UserId}", userId);
                throw new ApplicationException("Произошла внутренняя ошибка при обновлении профиля");
            }
        }
    }
}