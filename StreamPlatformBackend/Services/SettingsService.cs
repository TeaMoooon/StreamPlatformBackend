using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services.NotificationService;
using System.Globalization;

namespace StreamPlatformBackend.Services
{
    public interface ISettingsService 
    {
        // User settings
        Task UpdateUserProfileAsync(int userId, UserUpdateDataDto dto);
        Task<string> RegenerateStreamKeyAsync(int userId);
        
        // Stream settings
        Task<bool> UpdateStreamSettingsAsync(int userId, StreamUpdateDto dto);
        Task<string> UploadStreamPreviewForUserAsync(int userId, IFormFile file);
        Task<(List<StreamCategoryForSettingsDto> categories, int totalCount)> GetCategoriesAsync(string? search, int page, int pageSize);



    }
    public class SettingsService : ISettingsService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ILogger<SettingsService> _logger;
        private readonly string _mediaPath;
        public SettingsService(AppDbContext context, IConfiguration configuration, 
                               IPasswordHasherService passwordHasher, INotificationRepository notificationRepository, 
                               INotificationSender notificationSender, ILogger<SettingsService> logger)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _logger = logger;
            _mediaPath = configuration["Media:Path"];
        }

        // ==================================================
        // User settings
        // ==================================================
        public async Task UpdateUserProfileAsync(int userId, UserUpdateDataDto dto)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.SocialLinks)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                    throw new ArgumentException("Пользователь не найден");

                bool hasChanges = false;

                hasChanges |= await UpdateEmailAsync(user, dto.Email);
                hasChanges |= await UpdateNicknameAsync(user, dto.Nickname);
                hasChanges |= UpdateProfileDescription(user, dto.ProfileDescription);
                hasChanges |= UpdateSocialLinks(user, dto.SocialLinks);
                hasChanges |= UpdateRecordEnabled(user, dto.RecordEnabled);
                hasChanges |= await UpdatePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

                // Файлы
                if (dto.ProfileImage != null)
                {
                    await UpdateProfileImageAsync(user, dto.ProfileImage);
                    hasChanges = true;
                }

                if (dto.BackgroundImage != null)
                {
                    await UpdateBackgroundImageAsync(user, dto.BackgroundImage);
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




        public async Task<string> RegenerateStreamKeyAsync(int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _context.Users.FindAsync(userId) ?? throw new ArgumentException("Пользователь не найден");
                string newStreamKey;
                //int attempts = 0;
                //const int maxAttempts = 5;
                newStreamKey = GenerateStreamKey(userId);

                /*Закоментировано до выяснения необходимости из-за вида ключа (live_id_key)
                
                do
                {
                    newStreamKey = GenerateStreamKey(userId);
                    attempts++;
                    if (attempts > maxAttempts) throw new ApplicationException("Не удалось сгенерировать уникальный ключ трансляции");
                }
                while (await StreamKeyExistsAsync(newStreamKey));*/

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



        // ============== User private methods=========

        private async Task<bool> UpdateEmailAsync(UserModel user, string? email)
        {
            if (string.IsNullOrEmpty(email) || user.Email == email.ToLower())
                return false;

            if (!IsValidEmail(email))
                throw new ArgumentException("Неверный формат email");

            if (await EmailExistsAsync(email))
                throw new ArgumentException("Email уже используется");

            user.Email = email.ToLower();
            return true;
        }


        private async Task<bool> UpdateNicknameAsync(UserModel user, string? nickname)
        {
            if (string.IsNullOrEmpty(nickname) || user.Nickname == nickname.ToLower())
                return false;

            if (nickname.Length is < 3 or > 50)
                throw new ArgumentException("Никнейм должен быть от 3 до 50 символов");

            if (await NicknameExistsAsync(nickname))
                throw new ArgumentException("Никнейм уже используется");

            user.Nickname = nickname.ToLower();
            return true;
        }


        private bool UpdateProfileDescription(UserModel user, string? description)
        {
            if (description == null || user.ProfileDescription == description)
                return false;

            if (description.Length > 500)
                throw new ArgumentException("Описание не должно превышать 500 символов");

            user.ProfileDescription = description;
            return true;
        }


        private bool UpdateSocialLinks(UserModel user, List<SocialLinkDto>? links)
        {
            if (links == null)
                return false;

            bool changed = false;

            var toRemove = user.SocialLinks
                .Where(s => !links.Any(n => n.Platform == s.Platform))
                .ToList();

            if (toRemove.Any())
            {
                _context.UserSocialLinks.RemoveRange(toRemove);
                changed = true;
            }

            foreach (var newLink in links)
            {
                var existing = user.SocialLinks.FirstOrDefault(s => s.Platform == newLink.Platform);

                if (existing != null)
                {
                    if (existing.Url != newLink.Url)
                    {
                        existing.Url = newLink.Url;
                        changed = true;
                    }
                }
                else
                {
                    user.SocialLinks.Add(new UserSocialLink
                    {
                        UserId = user.Id,
                        Platform = newLink.Platform,
                        Url = newLink.Url
                    });
                    changed = true;
                }
            }

            return changed;
        }


        private bool UpdateRecordEnabled(UserModel user, bool recordEnabled)
        {
            if (user.RecordEnabled == recordEnabled)
                return false;

            user.RecordEnabled = recordEnabled;
            return true;
        }


        private async Task<bool> UpdatePasswordAsync(
                                    UserModel user,
                                    string? currentPassword,
                                    string? newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
                return false;

            if (string.IsNullOrEmpty(currentPassword))
                throw new ArgumentException("Текущий пароль обязателен для смены пароля");

            if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
                throw new ArgumentException("Текущий пароль неверен");

            if (newPassword.Length < 6)
                throw new ArgumentException("Новый пароль должен быть не менее 6 символов");

            user.PasswordHash = _passwordHasher.HashPassword(newPassword);
            return true;
        }
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
        private async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Email == email.ToLower());
        }
        private async Task<bool> NicknameExistsAsync(string nickname)
        {
            return await _context.Users.AsNoTracking().AnyAsync(u => u.Nickname == nickname.ToLower());
        }
        private static string GenerateStreamKey(int userId)
        {
            return $"live_{userId}_{Guid.NewGuid():N}";
        }
        private async Task UpdateProfileImageAsync(UserModel user, IFormFile file)
        {
            await SaveUserImageAsync(user, file, "profile");
        }
        private async Task UpdateBackgroundImageAsync(UserModel user, IFormFile file)
        {
            await SaveUserImageAsync(user, file, "background");
        }
        private async Task SaveUserImageAsync(UserModel user, IFormFile file, string type)
        {
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
            var folderPath = Path.Combine(baseMediaPath, "users", user.Id.ToString());
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
            string url = $"/media/users/{user.Id}/{fileName}";

            // Обновляем поля пользователя
            if (type == "profile") user.ProfileImage = url;
            if (type == "background") user.BackgroundImage = url;

        }


        /* Закоментировано до выяснения необходимости из-за вида ключа (live_id_key)

         private async Task<bool> StreamKeyExistsAsync(string streamKey)
        {
            return await _context.Users.AnyAsync(u => u.StreamKey == streamKey);
        }*/




        // ========================== Stream settings ===============================
        public async Task<bool> UpdateStreamSettingsAsync(int userId, StreamUpdateDto dto)
        {
            var stream = (await _context.Users.Include(u => u.CurrentStream).ThenInclude(s => s.Tags).FirstOrDefaultAsync(u => u.Id == userId))?.CurrentStream;
            if (stream == null) return false;

            if (!string.IsNullOrEmpty(dto.StreamName))
                await UpdateStreamNameAsync(stream, dto.StreamName);

            if (dto.PreviewImage != null)
                await UploadStreamPreviewAsync(stream, dto.PreviewImage);

            if (dto.CategoryId.HasValue)
                await UpdateStreamCategoryAsync(stream, dto.CategoryId.Value);

            if (dto.Tags != null)
                await UpdateStreamTagsAsync(stream, dto.Tags.ToList());

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<string> UploadStreamPreviewForUserAsync(int userId, IFormFile file)
        {
            var user = await _context.Users
                .Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId)
                ?? throw new ArgumentException("Пользователь не найден");

            var liveStream = user.CurrentStream is { EndedAt: null } stream ? stream : null;
            if (liveStream != null)
            {
                var url = await UploadStreamPreviewAsync(liveStream, file);
                user.LastPreviewUrl = url;
                await _context.SaveChangesAsync();
                return url;
            }

            var offlineUrl = await UploadOfflineStreamPreviewAsync(user, file);
            user.LastPreviewUrl = offlineUrl;
            await _context.SaveChangesAsync();
            return offlineUrl;
        }

        public async Task<(List<StreamCategoryForSettingsDto> categories, int totalCount)> GetCategoriesAsync(string? search, int page, int pageSize)
        {
            var query = _context.StreamCategories.AsQueryable();

            // Поиск только по имени
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name!.ToLower().Contains(search.ToLower()));

            int total = await query.CountAsync();

            var items = await query
                .OrderBy(c => c.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new StreamCategoryForSettingsDto
                {
                    Id = c.Id,
                    Name = c.Name!,
                    BannerImageUrl = c.BannerImageUrl
                })
                .ToListAsync();

            return (items, total);
        }









        // ====================== Stream private methods ========================
        private Task UpdateStreamNameAsync(StreamModel stream, string name)
        {
            stream.StreamName = name.Trim();
            return Task.CompletedTask;
        }

        private async Task UpdateStreamCategoryAsync(StreamModel stream, int categoryId)
        {
            var category = await _context.StreamCategories
                .FirstOrDefaultAsync(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            stream.CategoryId = category.Id;
        }

        private async Task UpdateStreamTagsAsync(StreamModel stream, List<string> slugs)
        {
            slugs = slugs
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLower())
                .Distinct()
                .ToList();

            var existingTags = await _context.Tags
                .Where(t => slugs.Contains(t.Slug))
                .ToListAsync();

            var missing = slugs.Except(existingTags.Select(t => t.Slug));
            foreach (var slug in missing)
            {
                var tag = new TagModel
                {
                    Slug = slug,
                    Name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug)
                };

                existingTags.Add(tag);
                _context.Tags.Add(tag);
            }

            await _context.SaveChangesAsync();

            var targetIds = existingTags.Select(t => t.Id).ToList();
            var currentIds = stream.Tags.Select(t => t.TagId).ToList();

            var toAdd = targetIds.Except(currentIds);
            foreach (var id in toAdd)
                stream.Tags.Add(new StreamTagModel { StreamId = stream.Id, TagId = id });

            var toRemove = currentIds.Except(targetIds);
            stream.Tags = new HashSet<StreamTagModel>(stream.Tags
                .Where(t => !toRemove.Contains(t.TagId)));
        }

        private async Task<string> UploadStreamPreviewAsync(StreamModel stream, IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Разрешены только JPG, PNG, WEBP");

            string baseMediaPath = _mediaPath ?? "/var/www/streamplatform/media";
            var folderPath = Path.Combine(baseMediaPath, "users", stream.UserId.ToString(), "streams", stream.Id.ToString());

            Directory.CreateDirectory(folderPath);

            string fileName = $"preview{extension}";
            string fullPath = Path.Combine(folderPath, fileName);

            using (var streamFile = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(streamFile);

            string url = $"/media/users/{stream.UserId}/streams/{stream.Id}/{fileName}";
            stream.PreviewUrl = url;

            return url;
        }

        private async Task<string> UploadOfflineStreamPreviewAsync(UserModel user, IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Разрешены только JPG, PNG, WEBP");

            string baseMediaPath = _mediaPath ?? "/var/www/streamplatform/media";
            var folderPath = Path.Combine(baseMediaPath, "users", user.Id.ToString());

            Directory.CreateDirectory(folderPath);

            string fileName = $"stream-preview{extension}";
            string fullPath = Path.Combine(folderPath, fileName);

            using (var streamFile = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(streamFile);

            return $"/media/users/{user.Id}/{fileName}";
        }



    }
}
