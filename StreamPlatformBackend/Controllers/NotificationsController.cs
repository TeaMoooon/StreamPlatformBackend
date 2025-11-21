using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Services.NotificationService;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly INotificationRepository _notificationRepository;

        public NotificationsController(AppDbContext db, INotificationRepository notificationRepository)
        {
            _db = db;
            _notificationRepository = notificationRepository;
        }

        /// <summary>
        /// Получить уведомления пользователя с пагинацией.
        /// </summary>
        /// <param name="page">Номер страницы, начиная с 1</param>
        /// <param name="limit">Количество уведомлений на странице</param>
        /// <returns>JSON с уведомлениями и общей информацией о пагинации</returns>
        /// <response code="200">Возвращает список уведомлений с пагинацией</response>
        /// <response code="401">Если пользователь не авторизован</response>
        [HttpGet]
        public async Task<IActionResult> GetNotifications(int page = 1, int limit = 5)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return Unauthorized();

            if (page < 1) page = 1;
            if (limit < 1) limit = 25;

            var totalCount = await _db.Notifications
                .CountAsync(n => n.UserId == userId);

            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .AsNoTracking()
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                limit,
                notifications = notifications.Select(n => new
                {
                    n.Id,
                    Type = n.Type.ToString(),
                    Payload = n.PayloadJson,
                    n.IsRead,
                    n.CreatedAt
                })
            });
        }

        /// <summary>
        /// Пометить выбранные уведомления как прочитанные.
        /// </summary>
        /// <param name="notificationIds">Массив ID уведомлений, которые нужно пометить</param>
        /// <returns>200 OK, если операция выполнена</returns>
        /// <response code="200">Уведомления успешно отмечены как прочитанные</response>
        /// <response code="401">Если пользователь не авторизован</response>
        [HttpPost("mark-read")]
        public async Task<IActionResult> MarkAsRead([FromBody] long[] notificationIds)
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return Unauthorized();

            await _notificationRepository.MarkAsReadAsync(userId, notificationIds);

            return Ok();
        }

        /// <summary>
        /// Пометить все уведомления пользователя как прочитанные.
        /// </summary>
        /// <returns>200 OK, если все уведомления успешно помечены</returns>
        /// <response code="200">Все уведомления отмечены как прочитанные</response>
        /// <response code="401">Если пользователь не авторизован</response>
        [HttpPost("mark-all-read")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return Unauthorized();

            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in notifications)
                n.IsRead = true;

            await _db.SaveChangesAsync();

            return Ok();
        }

        /// <summary>
        /// Получить количество непрочитанных уведомлений пользователя.
        /// </summary>
        /// <returns>JSON с полем count, показывающим число непрочитанных уведомлений</returns>
        /// <response code="200">Возвращает количество непрочитанных уведомлений</response>
        /// <response code="401">Если пользователь не авторизован</response>
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0) return Unauthorized();

            var count = await _db.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Ok(new { count });
        }

        /// <summary>
        /// Получить ID текущего пользователя из токена.
        /// </summary>
        /// <returns>ID пользователя или 0, если не найден</returns>
        private int GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : 0;
        }
    }
}
