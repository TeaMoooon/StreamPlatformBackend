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


        //[HttpPost("login")]
        /*public async Task<IActionResult> Login([FromBody] UserLoginDto loginDto)
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

                // Здесь будет логика генерации JWT токена
                return Ok(new
                {
                    message = "Вход выполнен успешно",
                    user = new { user.Id, user.Email, user.Nickname }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при входе пользователя {Email}", loginDto.Email);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }*/
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



        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (int.TryParse(userIdClaim, out int userId) && userId > 0)
            {
                return userId;
            }

            throw new UnauthorizedAccessException("Невалидный ID пользователя");
        }

    }
}
