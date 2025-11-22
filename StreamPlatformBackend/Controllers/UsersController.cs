using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<UsersController> _logger;
        private readonly IJwtService _jwtService;

        public UsersController(IUserService userService, IJwtService jwtService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _jwtService = jwtService;
            _logger = logger;
        }

        /// <summary>
        /// Регистрация нового пользователя.
        /// </summary>
        /// <param name="dto">Данные для регистрации.</param>
        /// <response code="200">Пользователь успешно зарегистрирован. Возвращает id, email, nickname.</response>
        /// <response code="400">Неверные входные данные или email/nickname уже заняты.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserCreateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var user = await _userService.CreateUserAsync(dto);
                return Ok(new
                {
                    user.Id,
                    user.Email,
                    user.Nickname
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Ошибка регистрации: {Message}", ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при регистрации пользователя с email {Email}", dto.Email);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Вход пользователя (валидация логина). Возвращает JWT токен.
        /// </summary>
        /// <param name="dto">Email и пароль.</param>
        /// <response code="200">Авторизация успешна. Возвращается токен.</response>
        /// <response code="401">Неверный email или пароль.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto dto)
        {
            var user = await _userService.LoginAsync(dto.LoginOrEmail, dto.Password);

            if (user == null)
                return Unauthorized(new { message = "Invalid login/email or password" });

            var token = _jwtService.GenerateToken(user);

            return Ok(new { token });
        }



        /// <summary>
        /// Получить публичный профиль по никнейму.
        /// </summary>
        /// <param name="nickname">Никнейм пользователя.</param>
        /// <response code="200">Возвращает публичный профиль.</response>
        /// <response code="404">Пользователь не найден.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [HttpGet("by-nickname/{nickname}")]
        public async Task<IActionResult> GetPublicProfileByNickname(string nickname)
        {
            try
            {
                var user = await _userService.GetUserByNameAsync(nickname);
                if (user == null) return NotFound(new { message = "Пользователь не найден" });

                var dto = new UserPublicProfileDto
                {
                    Id = user.Id,
                    Nickname = user.Nickname,
                    ProfileDescription = user.ProfileDescription,
                    BackgroundImage = user.BackgroundImage,
                    ProfileImage = user.ProfileImage,
                    RegistrationDate = user.RegistrationDate,
                    IsOnline = user.IsOnline,
                    CurrentStream = user.CurrentStream
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении публичного профиля для никнейма {Nickname}", nickname);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить личный профиль текущего авторизованного пользователя.
        /// </summary>
        /// <response code="200">Возвращает личный профиль.</response>
        /// <response code="401">Пользователь не авторизован.</response>
        /// <response code="404">Пользователь не найден.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetMyProfile()
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null) return NotFound(new { message = "Пользователь не найден" });

                var dto = new UserProfileDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    Nickname = user.Nickname,
                    ProfileDescription = user.ProfileDescription,
                    BackgroundImage = user.BackgroundImage,
                    ProfileImage = user.ProfileImage,
                    RegistrationDate = user.RegistrationDate,
                    CashBalance = user.CashBalance,
                    IsOnline = user.IsOnline
                };

                return Ok(dto);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Не удалось получить ID текущего пользователя: {Message}", ex.Message);
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении профиля текущего пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Обновление профиля текущего пользователя.
        /// </summary>
        /// <param name="dto">Данные для обновления (частичные — null поля игнорируются).</param>
        /// <response code="200">Профиль успешно обновлён.</response>
        /// <response code="400">Неверные данные.</response>
        /// <response code="401">Не авторизован.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UserUpdateDataDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var userId = GetCurrentUserId();
                await _userService.UpdateUserProfileAsync(userId, dto);
                return Ok(new { message = "Профиль успешно обновлён" });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Ошибка при обновлении профиля пользователя {UserId}: {Message}", GetCurrentUserIdSafe(), ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя {UserId}", GetCurrentUserIdSafe());
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Пересоздать StreamKey для текущего пользователя.
        /// </summary>
        /// <response code="200">Возвращает новый streamKey.</response>
        /// <response code="401">Не авторизован.</response>
        /// <response code="404">Пользователь не найден.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [Authorize]
        [HttpPost("stream-key/regenerate")]
        public async Task<IActionResult> RegenerateStreamKey()
        {
            try
            {
                var userId = GetCurrentUserId();
                var newKey = await _userService.RegenerateStreamKeyAsync(userId);
                return Ok(new { streamKey = newKey });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Ошибка при пересоздании streamKey для {UserId}: {Message}", GetCurrentUserIdSafe(), ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при пересоздании streamKey для {UserId}", GetCurrentUserIdSafe());
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить текущий streamKey (только для авторизованного пользователя).
        /// </summary>
        /// <response code="200">Возвращает streamKey.</response>
        /// <response code="401">Не авторизован.</response>
        /// <response code="404">Пользователь не найден.</response>
        [Authorize]
        [HttpGet("stream-key")]
        public async Task<IActionResult> GetMyStreamKey()
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null) return NotFound(new { message = "Пользователь не найден" });

                return Ok(new { streamKey = user.StreamKey });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении streamKey для {UserId}", GetCurrentUserIdSafe());
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить список онлайн-стримеров (публичный).
        /// </summary>
        [HttpGet("online/streamers")]
        public async Task<IActionResult> GetOnlineStreamers()
        {
            try
            {
                var list = await _userService.GetOnlineStreamersAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка онлайн-стримеров");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить количество онлайн-пользователей (публичный).
        /// </summary>
        [HttpGet("online/count")]
        public async Task<IActionResult> GetOnlineUsersCount()
        {
            try
            {
                var count = await _userService.GetOnlineUsersCountAsync();
                return Ok(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подсчёте онлайн-пользователей");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Проверка существования email (публичный — удобен для валидации на клиенте).
        /// </summary>
        [HttpGet("exists/email/{email}")]
        public async Task<IActionResult> CheckEmail(string email)
        {
            try
            {
                var exists = await _userService.EmailExistsAsync(email);
                return Ok(new { exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке существования email {Email}", email);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Проверка существования никнейма (публичный).
        /// </summary>
        [HttpGet("exists/nickname/{nickname}")]
        public async Task<IActionResult> CheckNickname(string nickname)
        {
            try
            {
                var exists = await _userService.NicknameExistsAsync(nickname);
                return Ok(new { exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке существования никнейма {Nickname}", nickname);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        // -------------------------
        // Helpers
        // -------------------------
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }

        // Возвращает id если он есть, иначе -1 (для логирования в catch-блоках)
        private int GetCurrentUserIdSafe()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var id)) return id;
            return -1;
        }
    }
}
