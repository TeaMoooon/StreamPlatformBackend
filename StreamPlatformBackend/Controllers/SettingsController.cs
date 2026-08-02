using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;
using System.Text.Json;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/settings")]
    [Authorize]
    [Produces("application/json")]
    public class SettingsController : ControllerBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IUserService _userService;
        private readonly IChatNicknameNotifier _chatNicknameNotifier;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(
            ISettingsService settingsService,
            IUserService userService,
            IChatNicknameNotifier chatNicknameNotifier,
            ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _userService = userService;
            _chatNicknameNotifier = chatNicknameNotifier;
            _logger = logger;
        }

        // ==================================================
        // User settings
        // ==================================================

        /// <summary>
        /// Обновление профиля пользователя, включая аватар и фон.
        /// </summary>
        /// <remarks>
        /// Поддерживает multipart/form-data для загрузки изображений.
        /// Можно обновлять: nickname, email, описание профиля, социальные ссылки, пароль, аватар и фон.
        /// </remarks>
        /// <response code="200">Профиль успешно обновлён</response>
        /// <response code="400">Ошибка валидации данных</response>
        /// <response code="401">Неавторизованный доступ</response>
        /// <response code="500">Внутренняя ошибка сервера</response>
        [HttpPut("profile")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateUserProfile([FromForm] UserUpdateDataDto dto)
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());

            try
            {
                if (Request.Form.TryGetValue("SocialLinks", out var socialLinksRaw) &&
                    !string.IsNullOrWhiteSpace(socialLinksRaw))
                {
                    dto.SocialLinks = JsonSerializer.Deserialize<List<SocialLinkDto>>(
                        socialLinksRaw.ToString(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new List<SocialLinkDto>();
                }

                var userBeforeUpdate = await _userService.GetUserByIdAsync(userId);
                var previousNickname = userBeforeUpdate?.Nickname;

                await _settingsService.UpdateUserProfileAsync(userId, dto);

                if (!string.IsNullOrWhiteSpace(dto.Nickname) && previousNickname != null)
                {
                    var normalizedNew = dto.Nickname.Trim().ToLowerInvariant();
                    if (!string.Equals(previousNickname, normalizedNew, StringComparison.Ordinal))
                    {
                        await _chatNicknameNotifier.NotifyNicknameChangedAsync(userId, normalizedNew);
                    }
                }

                _logger.LogInformation("Профиль пользователя {UserId} успешно обновлён", userId);

                return Ok(new { message = "Профиль успешно обновлён" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации при обновлении профиля пользователя {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Попытка неавторизованного обновления профиля");
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось обновить профиль пользователя {UserId}", userId);
                return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Генерация нового StreamKey для пользователя.
        /// </summary>
        /// <response code="200">Возвращает новый ключ стрима</response>
        /// <response code="401">Неавторизованный доступ</response>
        /// <response code="500">Ошибка генерации ключа</response>
        [HttpPut("streamkey")]
        [EnableRateLimiting("sensitive")]
        public async Task<IActionResult> RegenerateStreamKey()
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());


            try
            {
                var newKey = await _settingsService.RegenerateStreamKeyAsync(userId);

                _logger.LogInformation("StreamKey пользователя {UserId} успешно сгенерирован", userId);

                return Ok(new { streamKey = newKey });
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Неавторизованный доступ при генерации StreamKey");
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось сгенерировать StreamKey для пользователя {UserId}", userId);
                return StatusCode(500, new { error = "Не удалось сгенерировать ключ" });
            }
        }

        // ==================================================
        // Stream settings
        // ==================================================

        /// <summary>
        /// Обновление настроек стрима: имя, категория, теги, превью.
        /// </summary>
        /// <remarks>
        /// Поддерживает multipart/form-data для загрузки превью-изображения стрима.
        /// Все поля необязательные — можно обновлять частично.
        /// </remarks>
        /// <response code="200">Настройки стрима обновлены</response>
        /// <response code="400">Ошибка валидации данных</response>
        /// <response code="401">Неавторизованный доступ</response>
        /// <response code="404">Стрим не найден</response>
        /// <response code="500">Внутренняя ошибка сервера</response>
        [HttpPut("stream")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateStreamSettings([FromForm] StreamUpdateDto dto)
        {
            int userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException());


            try
            {
                var updated = await _settingsService.UpdateStreamSettingsAsync(userId, dto);

                if (!updated)
                {
                    _logger.LogWarning("Стрим пользователя {UserId} не найден", userId);
                    return NotFound(new { error = "Стрим не найден" });
                }

                _logger.LogInformation("Настройки стрима пользователя {UserId} успешно обновлены", userId);

                return Ok(new { message = "Настройки стрима обновлены" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Ошибка валидации при обновлении стрима пользователя {UserId}", userId);
                return BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Неавторизованный доступ при обновлении стрима");
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось обновить стрим пользователя {UserId}", userId);
                return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получение списка категорий для выбора в настройках стрима.
        /// </summary>
        /// <remarks>
        /// Поддерживает поиск по имени категории.
        /// Возвращает данные с пагинацией в том же формате, что и список стримов.
        /// </remarks>
        /// <param name="search">Поиск по названию категории</param>
        /// <param name="page">Номер страницы (по умолчанию 1)</param>
        /// <param name="pageSize">Размер страницы (по умолчанию 20)</param>
        /// <response code="200">Возвращает список категорий</response>
        /// <response code="401">Неавторизованный доступ</response>
        /// <response code="500">Внутренняя ошибка сервера</response>
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 20;

                var (categories, total) = await _settingsService.GetCategoriesAsync(search, page, pageSize);

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCategories = total,
                    Categories = categories
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка категорий");
                return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
            }
        }


    }
}
