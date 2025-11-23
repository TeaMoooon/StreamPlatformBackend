using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO;
using StreamPlatformBackend.DTO.StreamDTO;
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
        private readonly IStreamService _streamService;


        public UsersController(IUserService userService, IStreamService streamService, IJwtService jwtService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _jwtService = jwtService;
            _logger = logger;
            _streamService = streamService;
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
        /// Загружает новое изображение профиля (аватар) пользователя.
        /// </summary>
        /// <remarks>
        /// Эндпоинт принимает файл изображения в формате <b>multipart/form-data</b>.
        /// Разрешённые форматы: <b>JPG, JPEG, PNG, WEBP</b>.
        /// 
        /// Пример запроса:
        /// POST /api/users/upload/profile-image
        /// 
        /// FormData:
        ///  - file: (binary) изображение
        /// </remarks>
        /// <param name="file">Файл изображения</param>
        /// <returns>URL загруженного изображения</returns>
        /// <response code="200">Изображение успешно загружено</response>
        /// <response code="400">Файл не передан или неверный формат</response>
        /// <response code="401">Пользователь не авторизован</response>
        [Authorize]
        [HttpPost("upload/profile-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadProfileImage([FromForm] UploadImageDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { message = "Файл не передан" });

            var userId = GetCurrentUserId();
            var imageUrl = await _userService.UploadUserImageAsync(userId, dto.File, "profile");

            return Ok(new { imageUrl });
        }



        /// <summary>
        /// Загружает фоновое изображение профиля пользователя.
        /// </summary>
        /// <remarks>
        /// Принимает изображение через <b>multipart/form-data</b>.
        /// Разрешённые форматы: <b>JPG, JPEG, PNG, WEBP</b>.
        /// 
        /// Пример запроса:
        /// POST /api/users/upload/background-image
        /// 
        /// FormData:
        ///  - file: (binary) изображение
        /// </remarks>
        /// <param name="file">Файл изображения</param>
        /// <returns>URL загруженного изображения</returns>
        /// <response code="200">Изображение успешно загружено</response>
        /// <response code="400">Файл не передан или неверный формат</response>
        /// <response code="401">Пользователь не авторизован</response>
        [Authorize]
        [HttpPost("upload/background-image")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadBackgroundImage([FromForm] UploadImageDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { message = "Файл не передан" });

            var userId = GetCurrentUserId();
            var imageUrl = await _userService.UploadUserImageAsync(userId, dto.File, "background");

            return Ok(new { imageUrl });
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
        /// Получить список текущих стримов (публичный, с пагинацией).
        /// </summary>
        /// <param name="page">Номер страницы (по умолчанию 1)</param>
        /// <param name="pageSize">Количество стримов на страницу (по умолчанию 25)</param>
        [HttpGet("online/streams")]
        public async Task<IActionResult> GetOnlineStreams(int page = 1, int pageSize = 25)
        {
            try
            {
                var (streams, totalCount) = await _userService.GetOnlineStreamersAsync(page, pageSize);

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalStreams = totalCount,
                    Streams = streams
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка текущих стримов");
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


        /// <summary>
        /// Получить историю стримов пользователя по никнейму (публично, без авторизации).
        /// </summary>
        /// <remarks>
        /// Возвращает список всех стримов пользователя с пагинацией.
        /// </remarks>
        /// <param name="nickname">Никнейм пользователя.</param>
        /// <param name="page">Номер страницы (по умолчанию 1).</param>
        /// <param name="pageSize">Количество стримов на страницу (по умолчанию 25).</param>
        /// <response code="200">Возвращает список стримов пользователя с пагинацией.</response>
        /// <response code="404">Пользователь с указанным никнеймом не найден.</response>
        /// <response code="500">Внутренняя ошибка сервера.</response>
        [HttpGet("{nickname}/streams/history")]
        public async Task<IActionResult> GetUserStreamHistory(string nickname, int page = 1, int pageSize = 25)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 25;

                var user = await _userService.GetUserByNameAsync(nickname);
                if (user == null)
                    return NotFound(new { message = "Пользователь не найден" });

                // Получаем все стримы пользователя
                var streams = await _userService.GetUserStreamHistoryAsync(user.Id);

                // Сортировка по дате начала стрима (самые новые первыми)
                var sortedStreams = streams.OrderByDescending(s => s.StartedAt).ToList();

                // Пагинация
                var pagedStreams = sortedStreams
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Формируем DTO
                var result = pagedStreams.Select(s => new StreamInfoDto
                {
                    StreamId = s.Id,
                    StreamName = s.StreamName,
                    StreamerId = s.UserId,
                    StreamerName = s.User.Nickname,
                    Tags = s.Tags,
                    PreviewUrl = s.PreviewUrl,
                    HlsUrl = $"/hls/{s.User.StreamKey}.m3u8",
                    TotalViews = s.TotalViews,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    IsLive = s.EndedAt == null
                }).ToList();

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalStreams = sortedStreams.Count,
                    Streams = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении истории стримов пользователя {Nickname}", nickname);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }



    }
}
