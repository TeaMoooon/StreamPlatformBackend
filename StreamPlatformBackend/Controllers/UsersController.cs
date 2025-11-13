using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Services;
using System.Security.Claims;


namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/user")]
    public class UsersController : ControllerBase
    {

        private readonly IUserService _userService;
        private readonly IJwtService _jwtService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(IUserService userService, IJwtService jwtService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _jwtService = jwtService;
            _logger = logger;
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserCreateDto userCreateDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = await _userService.CreateUserAsync(userCreateDto);

                return Ok(new
                {
                    message = "Пользователь успешно зарегистрирован",
                    userId = user.Id
                });
            }
            catch (ArgumentException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (ApplicationException ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }



        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var isValid = await _userService.ValidateUserCredentialsAsync(
                    loginDto.Email, loginDto.Password);

                if (!isValid)
                {
                    return Unauthorized(new { message = "Неверный email или пароль" });
                }

                var user = await _userService.GetUserByEmailAsync(loginDto.Email);

                if (user == null)
                {
                    return Unauthorized(new { message = "Неверный email или пароль" });
                }

                // ⭐ ГЕНЕРИРУЕМ JWT ТОКЕН ⭐
                var token = _jwtService.GenerateToken(user);

                return Ok(new
                {
                    message = "Вход выполнен успешно",
                    token = token,
                    user = new
                    {
                        user.Id,
                        user.Email,
                        user.Nickname,
                        user.Role
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при входе пользователя {Email}", loginDto.Email);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }

                return Ok(new UserProfileDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    Nickname = user.Nickname,
                    ProfileDescription = user.ProfileDescription,
                    ProfileImage = user.ProfileImage,
                    RegistrationDate = user.RegistrationDate,
                    IsOnline = user.IsOnline
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении профиля пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }


        [HttpGet("public-profile-nickname")]
        public async Task<IActionResult> GetPublicProfileByName(string nickname)
        {
            try
            {
                
                var user = await _userService.GetUserByNameAsync(nickname);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }

                return Ok(new UserPublicProfileDto
                {
                    Id = user.Id,
                    Nickname = user.Nickname,
                    ProfileDescription = user.ProfileDescription,
                    ProfileImage = user.ProfileImage,
                    RegistrationDate = user.RegistrationDate,
                    IsOnline = user.IsOnline,
                    CurrentStream = user.CurrentStream

                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении профиля пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        [HttpGet("public-profile-id")]
        public async Task<IActionResult> GetPublicProfileById(int userId)
        {
            try
            {

                var user = await _userService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }

                return Ok(new UserPublicProfileDto
                {
                    Id = user.Id,
                    Nickname = user.Nickname,
                    ProfileDescription = user.ProfileDescription,
                    ProfileImage = user.ProfileImage,
                    RegistrationDate = user.RegistrationDate,
                    IsOnline = user.IsOnline,
                    CurrentStream = user.CurrentStream

                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении профиля пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateDataDto updateDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var userId = GetCurrentUserId();

                await _userService.UpdateUserProfileAsync(userId, updateDto);

                return Ok(new { message = "Профиль успешно обновлен" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении профиля пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }


        //[HttpPut]

        //[HttpPatch]

        [Authorize]
        [HttpPost("stream-key/regenerate")]
        public async Task<IActionResult> RegenerateStreamKey()
        {
            try
            {
                var userId = GetCurrentUserId();
                var newStreamKey = await _userService.RegenerateStreamKeyAsync(userId);

                return Ok(new
                {
                    message = "StreamKey успешно пересоздан",
                    streamKey = newStreamKey
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ApplicationException ex)
            {
                _logger.LogError(ex, "Ошибка при пересоздании StreamKey для пользователя {UserId}", GetCurrentUserId());
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при пересоздании StreamKey для пользователя {UserId}", GetCurrentUserId());
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        [Authorize]
        [HttpGet("stream-key")]
        public async Task<IActionResult> GetUserStreamKey()
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _userService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }

                return Ok(new
                {
                    streamKey = user.StreamKey,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении StreamKey для пользователя {UserId}", GetCurrentUserId());
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(userIdClaim, out int userId) && userId > 0)
            {
                return userId;
            }

            throw new UnauthorizedAccessException("Невалидный ID пользователя");
        }


        [HttpGet("online-users")]
        public async Task<ActionResult<IEnumerable<OnlineUserListDto>>> GetActiveStreams()
        {
            try
            {
                var activeStreams = await _userService.GetOnlineStreamersAsync();
                return Ok(activeStreams);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении активных стримов");
                return StatusCode(500, "Произошла ошибка при получении данных");
            }
        }

        [HttpGet("online-users/count")]
        public async Task<ActionResult<int>> GetActiveStreamsCount()
        {
            try
            {
                var count = await _userService.GetOnlineUsersCountAsync();
                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении количества активных стримов");
                return StatusCode(500, "Произошла ошибка при получении данных");
            }
        }


        [HttpGet("{userId}/subscriptions")]
        public async Task<ActionResult<IEnumerable<OnlineUserListDto>>> GetUserSubscriptions(int userId)
        {
            try
            {
                var subscriptions = await _userService.GetUserSubscriptionsAsync(userId);
                return Ok(subscriptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении подписок пользователя {UserId}", userId);
                return StatusCode(500, "Произошла ошибка при получении данных");
            }
        }



        [HttpPost("subscribe/{targetUserId}")]
        [Authorize] // ← Требуем авторизацию
        public async Task<ActionResult> SubscribeToUser(int targetUserId)
        {
            // Получаем ID текущего авторизованного пользователя из токена
            var subscriberId = GetCurrentUserIdFromToken();

            try
            {
                var result = await _userService.SubscribeToUserAsync(subscriberId, targetUserId);

                if (!result)
                {
                    return BadRequest("Не удалось выполнить подписку");
                }

                return Ok(new { message = "Подписка успешно оформлена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке пользователя {SubscriberId} на {TargetUserId}",
                    subscriberId, targetUserId);
                return StatusCode(500, "Произошла ошибка при выполнении подписки");
            }
        }

        [HttpDelete("unsubscribe/{targetUserId}")]
        [Authorize] // ← Требуем авторизацию
        public async Task<ActionResult> UnsubscribeFromUser(int targetUserId)
        {
            var subscriberId = GetCurrentUserIdFromToken();

            try
            {
                var result = await _userService.UnsubscribeFromUserAsync(subscriberId, targetUserId);

                if (!result)
                {
                    return BadRequest("Не удалось отписаться");
                }

                return Ok(new { message = "Подписка успешно отменена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке пользователя {SubscriberId} от {TargetUserId}",
                    subscriberId, targetUserId);
                return StatusCode(500, "Произошла ошибка при отписке");
            }
        }

        [HttpGet("is-subscribed/{targetUserId}")]
        [Authorize]
        public async Task<ActionResult<bool>> IsSubscribed(int targetUserId)
        {
            var currentUserId = GetCurrentUserIdFromToken();

            try
            {
                var isSubscribed = await _userService.IsSubscribedAsync(currentUserId, targetUserId);
                return Ok(isSubscribed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке подписки {CurrentUserId} на {TargetUserId}",
                    currentUserId, targetUserId);
                return StatusCode(500, "Произошла ошибка при проверке подписки");
            }
        }

        // Метод для получения ID текущего пользователя из JWT токена
        private int GetCurrentUserIdFromToken()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            }
            return userId;
        }

    }
}
