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
        Task UpdateUserProfileAsync(int userId, UserUpdateDataDto dto);
        Task<string> UploadUserImageAsync(int userId, IFormFile file, string type);

        Task<string> RegenerateStreamKeyAsync(int userId);
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

        public async Task UpdateUserProfileAsync(int userId, UserUpdateDataDto dto)
        {
            try
            {
                var user = await _context.Users.Include(u => u.SocialLinks)
                                               .FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null) throw new ArgumentException("Пользователь не найден");

                bool hasChanges = false;

                // 🔹 Email
                if (!string.IsNullOrEmpty(dto.Email) && user.Email != dto.Email.ToLower())
                {
                    if (!IsValidEmail(dto.Email))
                        throw new ArgumentException("Неверный формат email");
                    if (await EmailExistsAsync(dto.Email))
                        throw new ArgumentException("Email уже используется");

                    user.Email = dto.Email.ToLower();
                    hasChanges = true;
                }

                // 🔹 Nickname
                if (!string.IsNullOrEmpty(dto.Nickname) && user.Nickname != dto.Nickname.ToLower())
                {
                    if (dto.Nickname.Length is < 3 or > 50)
                        throw new ArgumentException("Никнейм должен быть от 3 до 50 символов");
                    if (await NicknameExistsAsync(dto.Nickname))
                        throw new ArgumentException("Никнейм уже используется");

                    user.Nickname = dto.Nickname.ToLower();
                    hasChanges = true;
                }

                // 🔹 ProfileDescription
                if (dto.ProfileDescription != null && user.ProfileDescription != dto.ProfileDescription)
                {
                    if (dto.ProfileDescription.Length > 500)
                        throw new ArgumentException("Описание не должно превышать 500 символов");

                    user.ProfileDescription = dto.ProfileDescription;
                    hasChanges = true;
                }

                // 🔹 SocialLinks
                if (dto.SocialLinks != null)
                {
                    var toRemove = user.SocialLinks
                                       .Where(s => !dto.SocialLinks.Any(n => n.Platform == s.Platform))
                                       .ToList();
                    _context.UserSocialLinks.RemoveRange(toRemove);

                    foreach (var newLink in dto.SocialLinks)
                    {
                        var existing = user.SocialLinks.FirstOrDefault(s => s.Platform == newLink.Platform);
                        if (existing != null)
                            existing.Url = newLink.Url;
                        else
                            user.SocialLinks.Add(new UserSocialLink
                            {
                                UserId = userId,
                                Platform = newLink.Platform,
                                Url = newLink.Url
                            });
                    }
                    hasChanges = true;
                }

                // 🔹 Change Password
                if (!string.IsNullOrEmpty(dto.NewPassword))
                {
                    if (string.IsNullOrEmpty(dto.CurrentPassword))
                        throw new ArgumentException("Текущий пароль обязателен для смены пароля");

                    if (!_passwordHasher.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
                        throw new ArgumentException("Текущий пароль неверен");

                    if (dto.NewPassword.Length < 6)
                        throw new ArgumentException("Новый пароль должен быть не менее 6 символов");

                    user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);
                    hasChanges = true;
                }

                // 🔹 RecordEnabled
                if (user.RecordEnabled != dto.RecordEnabled)
                {
                    user.RecordEnabled = dto.RecordEnabled;
                    hasChanges = true;
                }

                if (hasChanges)
                    await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя {UserId}", userId);
                throw;
            }
        }



        /// <summary>
        /// Загружает изображение на сервер и обновляет профиль пользователя.
        /// </summary>
        /// <remarks>
        /// Создаёт папку для пользователя, сохраняет файл с именем
        /// <b>profile.jpg</b> или <b>background.jpg</b>
        /// 
        /// Разрешённые форматы: JPG, JPEG, PNG, WEBP.
        /// </remarks>
        /// <param name="userId">ID пользователя</param>
        /// <param name="file">Файл изображения</param>
        /// <param name="type">Тип изображения ("profile" или "background")</param>
        /// <returns>URL сохранённого файла</returns>
        public async Task<string> UploadUserImageAsync(int userId, IFormFile file, string type)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                throw new ArgumentException("Пользователь не найден");

            // Разрешённые расширения
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Разрешены только форматы: JPG, PNG, WEBP");

            // Папка для хранения медиа конкретного пользователя
            // Берём путь из конфигурации, fallback если не задан
            string baseMediaPath = _mediaPath ?? "/var/www/streamplatform/media";
            var folderPath = Path.Combine(baseMediaPath, "users", userId.ToString());
            Directory.CreateDirectory(folderPath);

            // Имя файла
            string fileName = type switch
            {
                "profile" => $"profile{extension}",
                "background" => $"background{extension}",
                _ => throw new ArgumentException("Неверный тип изображения")
            };

            var fullPath = Path.Combine(folderPath, fileName);

            // Сохраняем файл
            using (var stream = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(stream);

            // URL для фронтенда (через Nginx /media/)
            string url = $"/media/users/{userId}/{fileName}";

            // Обновляем поля пользователя
            if (type == "profile") user.ProfileImage = url;
            if (type == "background") user.BackgroundImage = url;

            await _context.SaveChangesAsync();

            return url;
        }




        // Вспомогательная проверка email
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
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
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Email == email.ToLower());
        }

        public async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Nickname == nickname.ToLower());
        }

        public async Task<UserModel?> GetUserByEmailAsync(string email)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email.ToLower());
        }

        public async Task<UserModel?> GetUserByNameAsync(string name)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Nickname == name.ToLower());
        }

        public async Task<UserModel?> GetUserByIdAsync(int id)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
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



        public async Task<List<StreamModel>> GetUserStreamHistoryAsync(int userId)
        {
            // Получаем все стримы пользователя, сортируя по дате начала (новые первыми)
            var streams = await _context.Streams
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartedAt)
                .ToListAsync();

            return streams;
        }






    }
}