using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using System.Collections.Concurrent;

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
        Task<UserModel?> GetUserByNameAsync(string name);
        Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto);
        Task<string> RegenerateStreamKeyAsync(int userId);
        Task<bool> StreamKeyExistsAsync(string streamKey);
        Task<IEnumerable<OnlineUserListDto>> GetOnlineStreamersAsync();
        Task<int> GetOnlineUsersCountAsync();
        Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId);

        Task<bool> SubscribeToUserAsync(int subscriberId, int targetUserId);
        Task<bool> UnsubscribeFromUserAsync(int subscriberId, int targetUserId);
        Task<bool> IsSubscribedAsync(int subscriberId, int targetUserId);




        Task<List<UserModel>> GetSubscribersAsync(int streamerId);
        Task UpdateUserOnlineStatusAsync(int userId, bool isOnline);
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
                .AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Nickname.ToLower() == nickname.ToLower());
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

        public async Task<UserModel?> GetUserByNameAsync(string name)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Nickname.ToLower() == name.ToLower());
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
                    Nickname = userCreateDto.Nickname.ToLower(),
                    PasswordHash = passwordHash,

                    //StreamKey = GenerateStreamKey(),
                    StreamServerUrl = "rtmp://your-server.com/live"
                };

                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();

                user.StreamKey = GenerateStreamKey(user.Id);
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

        private static string GenerateStreamKey(int userId)
        {
            return $"live_{userId}_{Guid.NewGuid():N}";
        }


        //Добавить сброс ключа 
        public async Task<string> RegenerateStreamKeyAsync(int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Начало пересоздания StreamKey для пользователя: {UserId}", userId);

                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("Пользователь с ID {UserId} не найден", userId);
                    throw new ArgumentException("Пользователь не найден");
                }

                string newStreamKey;
                int attempts = 0;
                const int maxAttempts = 5;

                // Генерируем уникальный ключ
                do
                {
                    newStreamKey = GenerateStreamKey(userId);
                    attempts++;

                    if (attempts > maxAttempts)
                    {
                        _logger.LogError("Не удалось сгенерировать уникальный StreamKey после {Attempts} попыток для пользователя {UserId}",
                            maxAttempts, userId);
                        throw new ApplicationException("Не удалось сгенерировать уникальный ключ трансляции");
                    }
                }
                while (await StreamKeyExistsAsync(newStreamKey));

                // Сохраняем новый ключ
                var oldStreamKey = user.StreamKey;
                user.StreamKey = newStreamKey;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("StreamKey успешно пересоздан для пользователя {UserId}", userId);

                return newStreamKey;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> StreamKeyExistsAsync(string streamKey)
        {
            return await _context.Users
                .AnyAsync(u => u.StreamKey == streamKey);
        }


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


        public async Task<IEnumerable<OnlineUserListDto>> GetOnlineStreamersAsync()
        {
            return await _context.Users
                .Where(u => u.IsOnline && u.CurrentStream != null) // Это уже означает "ведет стрим"
                .Include(u => u.CurrentStream)
                 //.OrderByDescending(u => u.CurrentStream!.Viewers) // По количеству зрителей
                 // .ThenBy(u => u.Nickname) // Потом по имени
                .OrderBy(u => u.Nickname)
                .Select(u => new OnlineUserListDto
                {
                    Nickname = u.Nickname,
                    ProfileImage = u.ProfileImage,
                    IsOnline = u.IsOnline,
                    StreamersLeague = u.StreamersLeague,
                    PreviewlUrl = u.CurrentStream.PreviewlUrl,
                    StreamName = u.CurrentStream.StreamName
                })
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<int> GetOnlineUsersCountAsync()
        {
            return await _context.Users
                .Where(u => u.IsOnline)
                .CountAsync(); // ← ТОЛЬКО COUNT, без загрузки данных
        }

        public async Task<int> GetOnlineStreamersCountAsync()
        {
            return await _context.Users
                .Where(u => u.IsOnline) // Количество активных стримеров
                .CountAsync();
        }


        public async Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId)
        {
            return await _context.Subscriptions
                .Where(s => s.SubscriberId == userId)
                .Include(s => s.TargetUser)
                .ThenInclude(u => u.CurrentStream)
                .OrderByDescending(s => s.TargetUser.IsOnline) // Сначала онлайн
                .ThenByDescending(s => s.SubscriptionDate)    // Потом новые подписки сначала
                .Select(s => new OnlineUserListDto
                {
                    Nickname = s.TargetUser.Nickname,
                    ProfileImage = s.TargetUser.ProfileImage,
                    IsOnline = s.TargetUser.IsOnline,
                    StreamersLeague = s.TargetUser.StreamersLeague,
                    PreviewlUrl = s.TargetUser.CurrentStream != null ? s.TargetUser.CurrentStream.PreviewlUrl : string.Empty,
                    StreamName = s.TargetUser.CurrentStream != null ? s.TargetUser.CurrentStream.StreamName : string.Empty
                })
                .AsNoTracking()
                .ToListAsync();
        }



        public async Task<bool> SubscribeToUserAsync(int subscriberId, int targetUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Проверяем, что пользователи существуют
                var subscriber = await _context.Users.FindAsync(subscriberId);
                var targetUser = await _context.Users.FindAsync(targetUserId);

                if (subscriber == null || targetUser == null)
                {
                    _logger.LogWarning("Попытка подписки с несуществующими пользователями: Subscriber={SubscriberId}, Target={TargetUserId}",
                        subscriberId, targetUserId);
                    return false;
                }

                // Проверяем, что не подписываемся на себя
                if (subscriberId == targetUserId)
                {
                    _logger.LogWarning("Пользователь {SubscriberId} попытался подписаться на себя", subscriberId);
                    return false;
                }

                // Проверяем, нет ли уже подписки
                var existingSubscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);

                if (existingSubscription != null)
                {
                    _logger.LogWarning("Пользователь {SubscriberId} уже подписан на {TargetUserId}", subscriberId, targetUserId);
                    return false;
                }

                // Создаем подписку
                var subscription = new SubscriptionModel
                {
                    SubscriberId = subscriberId,
                    TargetUserId = targetUserId,
                    SubscriptionDate = DateTime.UtcNow
                };

                await _context.Subscriptions.AddAsync(subscription);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Пользователь {SubscriberId} успешно подписался на {TargetUserId}",
                    subscriberId, targetUserId);

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Ошибка при подписке пользователя {SubscriberId} на {TargetUserId}",
                    subscriberId, targetUserId);
                return false;
            }
        }

        public async Task<bool> UnsubscribeFromUserAsync(int subscriberId, int targetUserId)
        {
            try
            {
                var subscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);

                if (subscription == null)
                {
                    _logger.LogWarning("Попытка отписаться от несуществующей подписки: Subscriber={SubscriberId}, Target={TargetUserId}",
                        subscriberId, targetUserId);
                    return false;
                }

                _context.Subscriptions.Remove(subscription);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Пользователь {SubscriberId} успешно отписался от {TargetUserId}",
                    subscriberId, targetUserId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке пользователя {SubscriberId} от {TargetUserId}",
                    subscriberId, targetUserId);
                return false;
            }
        }

        public async Task<bool> IsSubscribedAsync(int subscriberId, int targetUserId)
        {
            return await _context.Subscriptions
                .AnyAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);
        }






        public async Task<List<UserModel>> GetSubscribersAsync(int streamerId)
        {
            return await _context.Subscriptions
                .Where(s => s.TargetUserId == streamerId) // Подписчики на этого стримера
                .Include(s => s.Subscriber)
                .Select(s => s.Subscriber)
                .AsNoTracking()
                .ToListAsync();
        }
        public async Task UpdateUserOnlineStatusAsync(int userId, bool isOnline)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.IsOnline = isOnline;
                user.LastOnlineDate = isOnline ? null : DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}