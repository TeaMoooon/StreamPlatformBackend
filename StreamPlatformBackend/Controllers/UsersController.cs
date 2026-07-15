using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.DTO;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models.User;
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
        private readonly IPlatformSanctionService _platformSanctions;


        public UsersController(
            IUserService userService,
            IStreamService streamService,
            IJwtService jwtService,
            IPlatformSanctionService platformSanctions,
            ILogger<UsersController> logger)
        {
            _userService = userService;
            _jwtService = jwtService;
            _logger = logger;
            _streamService = streamService;
            _platformSanctions = platformSanctions;
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

            if (await _platformSanctions.BlocksLoginAsync(user.Id))
            {
                var message = await _platformSanctions.GetBlockMessageAsync(
                    user.Id,
                    PlatformSanctionTypes.LoginBan,
                    PlatformSanctionTypes.FullBan);

                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = message ?? "Аккаунт заблокирован"
                });
            }

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
                    ProfileImage = GetMediaUrl(user.ProfileImage),
                    BackgroundImage = GetMediaUrl(user.BackgroundImage),
                    RegistrationDate = user.RegistrationDate,
                    IsOnline = user.IsOnline,
                    CurrentStream = user.CurrentStream,
                    SocialLinks = user.SocialLinks.Select(link => new UserSocialLinkDto{
                                    Platform = link.Platform,
                                    Url = link.Url
                                    }).ToList()
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
                    ProfileImage = GetMediaUrl(user.ProfileImage),
                    BackgroundImage = GetMediaUrl(user.BackgroundImage),
                    RegistrationDate = user.RegistrationDate,
                    CashBalance = user.CashBalance,
                    IsOnline = user.IsOnline,
                    SocialLinks = user.SocialLinks.Select(link => new UserSocialLinkDto
                    {
                        Platform = link.Platform,
                        Url = link.Url
                    }).ToList()
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
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 25;

                // ✅ Получаем сразу DTO из сервиса
                var (streams, totalCount) = await _userService.GetOnlineStreamersAsync(page, pageSize);

                // Если нужно добавить StreamId или обработать PreviewUrl через метод контроллера
                var result = streams.Select(s => new OnlineUserListDto
                {
                    UserId = s.UserId,
                    Nickname = s.Nickname,
                    ProfileImage = GetMediaUrl(s.ProfileImage),
                    IsOnline = s.IsOnline,
                    StreamersLeague = s.StreamersLeague,
                    PreviewUrl = ResolveOnlinePreviewUrl(s),
                    StreamName = s.StreamName,
                    StreamId = s.StreamId,
                    TotalViews = s.TotalViews,
                    ViewerCount = StreamViewerStore.GetViewerCount(s.UserId)
                }).ToList();

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalStreams = totalCount,
                    Streams = result
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

        // Внутри UsersController
        private string GetMediaUrl(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.StartsWith("/") ? path : $"/media/{path}";
        }

        private async Task<UserModel> GetCurrentUserAsync()
        {
            var userId = GetCurrentUserId();
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) throw new ArgumentException("Пользователь не найден");
            return user;
        }

        private string GetStreamMediaUrl(int userId, int streamId, string? filename)
        {
            if (string.IsNullOrEmpty(filename)) return string.Empty;
            return filename.StartsWith("/") ? filename : $"/media/users/{userId}/streams/{streamId}/{filename}";
        }

        private string ResolveOnlinePreviewUrl(OnlineUserListDto stream)
        {
            if (!string.IsNullOrWhiteSpace(stream.PreviewUrl))
            {
                var preview = GetStreamMediaUrl(stream.UserId, stream.StreamId ?? 0, stream.PreviewUrl);
                if (!string.IsNullOrEmpty(preview))
                    return preview;
            }

            return GetMediaUrl(stream.ProfileImage);
        }


        /// <summary>
        /// Получает историю стримов указанного пользователя.
        /// Возвращает только стримы, для которых существует запись (архив).
        /// Поддерживает пагинацию.
        /// </summary>
        /// <param name="nickname">Никнейм стримера.</param>
        /// <param name="page">Номер страницы (начиная с 1).</param>
        /// <param name="pageSize">Количество элементов на странице.</param>
        /// <returns>
        /// Объект с информацией о странице и списком стримов:
        /// <para>• Page — номер текущей страницы</para>
        /// <para>• PageSize — количество элементов на странице</para>
        /// <para>• TotalStreams — общее количество стримов с записью</para>
        /// <para>• Streams — массив объектов StreamHistoryItemDto</para>
        /// </returns>
        /// <response code="200">История стримов успешно получена.</response>
        /// <response code="404">Пользователь с таким никнеймом не найден.</response>
        /// <response code="500">Ошибка на стороне сервера.</response>
        [HttpGet("{nickname}/streams/history")]
        public async Task<IActionResult> GetUserStreamHistory(
            string nickname, int page = 1, int pageSize = 25)
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

                // Фильтруем только стримы, где есть запись
                var recordedStreams = streams
                    .Where(s => !string.IsNullOrEmpty(s.RecordPath))
                    .OrderByDescending(s => s.StartedAt)
                    .ToList();

                // Пагинация
                var pagedStreams = recordedStreams
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // DTO
                var result = pagedStreams.Select(s => new StreamHistoryItemDto
                {
                    Id = s.Id,
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    HasRecord = !string.IsNullOrEmpty(s.RecordPath),
                    RecordPath = s.RecordPath,
                    StreamName = s.StreamName,
                    CategoryName = s.Category?.Name,
                    Tags = s.Tags.Select(st => st.Tag.Name).ToList()

                }).ToList();

                return Ok(new
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalStreams = recordedStreams.Count,
                    Streams = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Ошибка при получении истории стримов пользователя {Nickname}", nickname);

                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }





    }
}
