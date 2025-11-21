using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.Services.NotificationService;
using System.Security.Claims;
using System.Text.Json;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/subscriptions")]
    public class SubscriptionsController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<SubscriptionsController> _logger;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;

        public SubscriptionsController(IUserService userService, ILogger<SubscriptionsController> logger, INotificationRepository notificationRepository, NotificationSender notificationSender)
        {
            _userService = userService;
            _logger = logger;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
        }

        /// <summary>
        /// Подписаться на пользователя (требуется авторизация).
        /// </summary>
        /// <param name="targetUserId">ID пользователя, на которого подписываются.</param>
        /// <response code="200">Подписка выполнена.</response>
        /// <response code="400">Нельзя подписаться (например, уже подписан или self-subscribe).</response>
        /// <response code="401">Не авторизован.</response>
        [Authorize]
        [HttpPost("follow/{targetUserId:int}")]
        public async Task<IActionResult> Follow(int targetUserId)
        {
            try
            {
                var subscriberId = GetCurrentUserId();
                var ok = await _userService.SubscribeToUserAsync(subscriberId, targetUserId);
                if (!ok) return BadRequest(new { message = "Не удалось подписаться" });

                // 🔔 Создаём уведомление в базе через репозиторий
                var notification = await _notificationRepository.CreateNotificationAsync(new NotificationModel
                {
                    UserId = targetUserId, // стример, который получил нового подписчика
                    Type = NotificationType.NewFollower,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        SubscriberId = subscriberId,
                        SubscriberName = (await _userService.GetUserByIdAsync(subscriberId))?.Nickname
                    })
                });

                // 🔔 Отправка уведомления через SignalR
                await _notificationSender.SendToUserAsync(notification);

                return Ok(new { message = "Подписка оформлена" });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Отписаться от пользователя (требуется авторизация).
        /// </summary>
        /// <param name="targetUserId">ID пользователя, от которого отписываются.</param>
        /// <response code="200">Отписка выполнена.</response>
        /// <response code="400">Не удалось отписаться.</response>
        /// <response code="401">Не авторизован.</response>
        [Authorize]
        [HttpPost("unfollow/{targetUserId:int}")]
        public async Task<IActionResult> Unfollow(int targetUserId)
        {
            try
            {
                var subscriberId = GetCurrentUserId();
                var ok = await _userService.UnsubscribeFromUserAsync(subscriberId, targetUserId);
                if (!ok) return BadRequest(new { message = "Не удалось отписаться" });
                return Ok(new { message = "Подписка отменена" });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить список пользователей, на которых подписан текущий пользователь (требуется авторизация).
        /// </summary>
        [Authorize]
        [HttpGet("me/following")]
        public async Task<IActionResult> GetMyFollowing()
        {
            try
            {
                var userId = GetCurrentUserId();
                var list = await _userService.GetUserSubscriptionsAsync(userId);
                return Ok(list);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка подписок текущего пользователя");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получить список подписчиков данного стримера (публичный/или авторизованный в зависимости от политики).
        /// </summary>
        [HttpGet("{streamerId:int}/subscribers")]
        public async Task<IActionResult> GetSubscribers(int streamerId)
        {
            try
            {
                var subs = await _userService.GetSubscribersAsync(streamerId);
                return Ok(subs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении подписчиков для {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
