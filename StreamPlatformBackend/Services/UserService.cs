using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Stream;
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

        Task<(List<OnlineUserListDto> Streams, int TotalCount)> GetOnlineStreamersAsync(int page, int pageSize);
        Task<int> GetOnlineUsersCountAsync();
        Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId);
        Task<bool> SubscribeToUserAsync(int subscriberId, int targetUserId);
        Task<bool> UnsubscribeFromUserAsync(int subscriberId, int targetUserId);
        Task<bool> IsSubscribedAsync(int subscriberId, int targetUserId);
        Task UpdateUserOnlineStatusAsync(int userId, bool isOnline);
        Task<UserModel?> LoginAsync(string loginOrEmail, string password);

        Task<UserModel> GetUserByNameAsync(string name);
        Task<UserModel> GetUserByIdAsync(int id);

        Task<bool> NicknameExistsAsync(string nickname);
        Task<bool> EmailExistsAsync(string email);

        Task<List<UserModel>> GetSubscribersAsync(int streamerId);

        Task<List<int>> GetSubscribedStreamerIdsAsync(int userId);

        Task<UserModel> GetUserByEmailAsync(string email);

        Task<List<StreamModel>> GetUserStreamHistoryAsync(int userId);


    }

    public class UserService : IUserService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ILogger<UserService> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;
        private readonly string _mediaPath;

        public UserService(AppDbContext context, IConfiguration configuration, IPasswordHasherService passwordHasher, INotificationRepository notificationRepository, INotificationSender notificationSender, ILogger<UserService> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
            _mediaPath = configuration["Media:Path"];
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
                    Email = userCreateDto.Email.ToLower(),
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

        public async Task<UserModel?> LoginAsync(string loginOrEmail, string password)
        {
            UserModel? user;

            // Проверяем, что это email (если есть @)
            bool isEmail = loginOrEmail.Contains("@");

            if (isEmail)
            {
                user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == loginOrEmail.ToLower());
            }
            else
            {
                user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Nickname == loginOrEmail.ToLower());
            }

            if (user == null)
                return null;

            // Проверка пароля через BCrypt
            if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
                return null;

            return user;
        }

        public async Task<bool> ValidateUserCredentialsAsync(string email, string password)
        {
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower());
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

        



        




        // Вспомогательная проверка email
        






        

        public async Task<(List<OnlineUserListDto> Streams, int TotalCount)> GetOnlineStreamersAsync(int page = 1, int pageSize = 25)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;

            var query = _context.Users
                .Where(u => u.CurrentStream != null)
                .Include(u => u.CurrentStream)
                .OrderBy(u => u.Nickname)
                .AsNoTracking();

            int totalCount = await query.CountAsync();

            var list = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new OnlineUserListDto
                {
                    Nickname = u.Nickname,
                    ProfileImage = u.ProfileImage,
                    StreamersLeague = u.StreamersLeague,
                    PreviewUrl = u.CurrentStream.PreviewUrl,
                    StreamName = u.CurrentStream.StreamName
                })
                .ToListAsync();

            return (list, totalCount);
        }


        public async Task<int> GetOnlineUsersCountAsync()
        {
            return await _context.Users.CountAsync(u => u.IsOnline);
        }

        public async Task<IEnumerable<OnlineUserListDto>> GetUserSubscriptionsAsync(int userId)
        {
            return await _context.Subscriptions
                .Where(s => s.SubscriberId == userId && s.IsActive)
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
                    PreviewUrl = s.TargetUser.CurrentStream != null
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

                if (existingSubscription != null)
                {
                    if (existingSubscription.IsActive) return false;

                    existingSubscription.IsActive = true;
                    existingSubscription.SubscriptionDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }

                var subscription = new SubscriptionModel
                {
                    SubscriberId = subscriberId,
                    TargetUserId = targetUserId,
                    IsActive = true,
                    SubscriptionDate = DateTime.UtcNow
                };

                await _context.Subscriptions.AddAsync(subscription);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var notification = new NotificationModel
                {
                    UserId = targetUserId,
                    Type = NotificationType.NewFollower,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        SubscriberId = subscriberId,
                        SubscriberName = (await GetUserByIdAsync(subscriberId))?.Nickname
                    }),
                    CreatedAt = DateTime.UtcNow
                };

                await _notificationRepository.CreateNotificationAsync(notification);
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

                if (subscription == null || !subscription.IsActive) return false;

                subscription.IsActive = false;
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
            return await _context.Subscriptions.AnyAsync(s =>
                s.SubscriberId == subscriberId && s.TargetUserId == targetUserId && s.IsActive);
        }

        public async Task<List<UserModel>> GetSubscribersAsync(int streamerId)
        {
            return await _context.Subscriptions
                .Where(s => s.TargetUserId == streamerId && s.IsActive)
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
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Email == email.ToLower());
        }

        public async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Nickname == nickname.ToLower());
        }

        public async Task<UserModel?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.SocialLinks)
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Email == email.ToLower());
        }

        public async Task<UserModel?> GetUserByNameAsync(string name)
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.SocialLinks)
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Nickname.ToLower() == name.ToLower());
        }


        public async Task<UserModel?> GetUserByIdAsync(int id)
        {
            return await _context.Users
                .AsNoTracking()
                .Include(u => u.SocialLinks)
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == id);
        }


        public async Task<bool> StreamKeyExistsAsync(string streamKey)
        {
            return await _context.Users.AnyAsync(u => u.StreamKey == streamKey);
        }

        public async Task<List<int>> GetSubscribedStreamerIdsAsync(int userId)
        {
            // Берем TargetUserId всех подписок, где текущий пользователь — подписчик
            return await _context.Subscriptions
                .Where(s => s.SubscriberId == userId && s.IsActive)
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



        public async Task<List<StreamModel>> GetUserStreamHistoryAsync(int userId)
        {
            // Получаем все стримы пользователя, сортируя по дате начала (новые первыми)
            var streams = await _context.Streams
                .Where(s => s.UserId == userId)
                .Include(s => s.Category)        // подтягиваем категорию
                .Include(s => s.Tags)            // подтягиваем связи StreamTag
                .ThenInclude(st => st.Tag)   // подтягиваем сами теги
                .OrderByDescending(s => s.StartedAt)
                .ToListAsync();

            return streams;
        }






    }
}