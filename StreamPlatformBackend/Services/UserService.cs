using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services.NotificationService;
using System.Collections.Concurrent;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{
    public interface IUserService
    {
        Task<UserModel> CreateUserAsync(UserCreateDto userCreateDto);
        Task<bool> ValidateUserCredentialsAsync(string email, string password);
        Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto);
        Task<string> RegenerateStreamKeyAsync(int userId);
        Task<IEnumerable<OnlineUserListDto>> GetOnlineStreamersAsync();
        Task<int> GetOnlineUsersCountAsync();
        Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId);
        Task<bool> SubscribeToUserAsync(int subscriberId, int targetUserId);
        Task<bool> UnsubscribeFromUserAsync(int subscriberId, int targetUserId);
        Task<bool> IsSubscribedAsync(int subscriberId, int targetUserId);
        Task UpdateUserOnlineStatusAsync(int userId, bool isOnline);


        Task<UserModel> GetUserByNameAsync(string name);
        Task<UserModel> GetUserByIdAsync(int id);

        Task<bool> NicknameExistsAsync(string nickname);
        Task<bool> EmailExistsAsync(string email);

        Task<List<UserModel>> GetSubscribersAsync(int streamerId);

        Task<List<int>> GetSubscribedStreamerIdsAsync(int userId);

        Task<UserModel> GetUserByEmailAsync(string email);


    }

    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ILogger<UserService> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;

        public UserService(AppDbContext context, IPasswordHasherService passwordHasher, INotificationRepository notificationRepository, NotificationSender notificationSender, ILogger<UserService> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
        }


        // =========================
        // Публичные методы (API)
        // =========================
        public async Task<UserModel> CreateUserAsync(UserCreateDto userCreateDto)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (await EmailExistsAsync(userCreateDto.Email))
                    throw new ArgumentException("Email уже используется");

                if (await NicknameExistsAsync(userCreateDto.Nickname))
                    throw new ArgumentException("Никнейм уже используется");

                var passwordHash = _passwordHasher.HashPassword(userCreateDto.Password);

                var user = new UserModel
                {
                    Email = userCreateDto.Email,
                    Nickname = userCreateDto.Nickname.ToLower(),
                    PasswordHash = passwordHash,
                    StreamServerUrl = "rtmp://your-server.com/live"
                };

                await _context.Users.AddAsync(user);
                await _context.SaveChangesAsync();

                user.StreamKey = GenerateStreamKey(user.Id);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return user;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Ошибка при создании пользователя {Email}", userCreateDto.Email);
                throw;
            }
        }


        public async Task<bool> ValidateUserCredentialsAsync(string email, string password)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                {
                    await Task.Delay(2000);
                    return false;
                }

                var isValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);
                if (isValid)
                {
                    user.LastAuthDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке учетных данных {Email}", email);
                return false;
            }
        }

        public async Task UpdateUserProfileAsync(int userId, UserUpdateDataDto userUpdateDataDto)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) throw new ArgumentException("Пользователь не найден");

                bool hasChanges = false;

                if (!string.IsNullOrEmpty(userUpdateDataDto.Email) && user.Email != userUpdateDataDto.Email)
                {
                    if (await EmailExistsAsync(userUpdateDataDto.Email))
                        throw new ArgumentException("Email уже используется");
                    user.Email = userUpdateDataDto.Email;
                    hasChanges = true;
                }

                if (!string.IsNullOrEmpty(userUpdateDataDto.Nickname) && user.Nickname != userUpdateDataDto.Nickname)
                {
                    if (await NicknameExistsAsync(userUpdateDataDto.Nickname))
                        throw new ArgumentException("Никнейм уже используется");
                    user.Nickname = userUpdateDataDto.Nickname;
                    hasChanges = true;
                }

                if (userUpdateDataDto.ProfileDescription != null && user.ProfileDescription != userUpdateDataDto.ProfileDescription)
                {
                    user.ProfileDescription = userUpdateDataDto.ProfileDescription;
                    hasChanges = true;
                }

                if (userUpdateDataDto.ProfileImage != null && user.ProfileImage != userUpdateDataDto.ProfileImage)
                {
                    user.ProfileImage = userUpdateDataDto.ProfileImage;
                    hasChanges = true;
                }

                if (hasChanges) await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя {UserId}", userId);
                throw;
            }
        }

        public async Task<string> RegenerateStreamKeyAsync(int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _context.Users.FindAsync(userId) ?? throw new ArgumentException("Пользователь не найден");
                string newStreamKey;
                int attempts = 0;
                const int maxAttempts = 5;

                do
                {
                    newStreamKey = GenerateStreamKey(userId);
                    attempts++;
                    if (attempts > maxAttempts) throw new ApplicationException("Не удалось сгенерировать уникальный ключ трансляции");
                }
                while (await StreamKeyExistsAsync(newStreamKey));

                user.StreamKey = newStreamKey;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return newStreamKey;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Ошибка при пересоздании StreamKey для пользователя {UserId}", userId);
                throw;
            }
        }

        public async Task<IEnumerable<OnlineUserListDto>> GetOnlineStreamersAsync()
        {
            return await _context.Users
                .Where(u => u.IsOnline && u.CurrentStream != null)
                .Include(u => u.CurrentStream)
                .OrderBy(u => u.Nickname)
                .Select(u => new OnlineUserListDto
                {
                    Nickname = u.Nickname,
                    ProfileImage = u.ProfileImage,
                    IsOnline = u.IsOnline,
                    StreamersLeague = u.StreamersLeague,
                    PreviewlUrl = u.CurrentStream.PreviewUrl,
                    StreamName = u.CurrentStream.StreamName
                })
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<int> GetOnlineUsersCountAsync()
        {
            return await _context.Users.CountAsync(u => u.IsOnline);
        }

        public async Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId)
        {
            return await _context.Subscriptions
                .Where(s => s.SubscriberId == userId)
                .Include(s => s.TargetUser)
                .ThenInclude(u => u.CurrentStream)
                .OrderByDescending(s => s.TargetUser.IsOnline)
                .ThenByDescending(s => s.SubscriptionDate)
                .Select(s => new OnlineUserListDto
                {
                    Nickname = s.TargetUser.Nickname,
                    ProfileImage = s.TargetUser.ProfileImage,
                    IsOnline = s.TargetUser.IsOnline,
                    StreamersLeague = s.TargetUser.StreamersLeague,
                    PreviewlUrl = s.TargetUser.CurrentStream != null
                        ? s.TargetUser.CurrentStream.PreviewUrl
                        : string.Empty,

                    StreamName = s.TargetUser.CurrentStream != null
                        ? s.TargetUser.CurrentStream.StreamName
                        : string.Empty
                })
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<bool> SubscribeToUserAsync(int subscriberId, int targetUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (subscriberId == targetUserId) return false;

                var existingSubscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);

                if (existingSubscription != null) return false;

                var subscription = new SubscriptionModel
                {
                    SubscriberId = subscriberId,
                    TargetUserId = targetUserId,
                    SubscriptionDate = DateTime.UtcNow
                };

                await _context.Subscriptions.AddAsync(subscription);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();


                // 1) Сохраняем уведомление
                var notification = new NotificationModel
                {
                    UserId = targetUserId, // стример
                    Type = NotificationType.NewFollower,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        SubscriberId = subscriberId,
                        SubscriberName = (await GetUserByIdAsync(subscriberId))?.Nickname
                    }),
                    CreatedAt = DateTime.UtcNow
                };

                await _notificationRepository.CreateNotificationAsync(notification);

                // 2) Отправляем через SignalR
                await _notificationSender.SendToUserAsync(notification);

                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Ошибка при подписке пользователя {SubscriberId} на {TargetUserId}", subscriberId, targetUserId);
                return false;
            }
        }



        public async Task<bool> UnsubscribeFromUserAsync(int subscriberId, int targetUserId)
        {
            try
            {
                var subscription = await _context.Subscriptions
                    .FirstOrDefaultAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);

                if (subscription == null) return false;

                _context.Subscriptions.Remove(subscription);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке пользователя {SubscriberId} от {TargetUserId}", subscriberId, targetUserId);
                return false;
            }
        }

        public async Task<bool> IsSubscribedAsync(int subscriberId, int targetUserId)
        {
            return await _context.Subscriptions.AnyAsync(s => s.SubscriberId == subscriberId && s.TargetUserId == targetUserId);
        }

        public async Task<List<UserModel>> GetSubscribersAsync(int streamerId)
        {
            return await _context.Subscriptions
                .Where(s => s.TargetUserId == streamerId)
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

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Nickname.ToLower() == nickname.ToLower());
        }

        public async Task<UserModel?> GetUserByEmailAsync(string email)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<UserModel?> GetUserByIdAsync(int id)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        }

        public async Task<UserModel?> GetUserByNameAsync(string name)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Nickname.ToLower() == name.ToLower());
        }

        public async Task<bool> StreamKeyExistsAsync(string streamKey)
        {
            return await _context.Users.AnyAsync(u => u.StreamKey == streamKey);
        }

        public async Task<List<int>> GetSubscribedStreamerIdsAsync(int userId)
        {
            // Берем TargetUserId всех подписок, где текущий пользователь — подписчик
            return await _context.Subscriptions
                .Where(s => s.SubscriberId == userId)
                .Select(s => s.TargetUserId)
                .ToListAsync();
        }

        // =========================
        // Приватные методы
        // =========================

        private static string GenerateStreamKey(int userId)
        {
            return $"live_{userId}_{Guid.NewGuid():N}";
        }






    }
}