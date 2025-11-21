using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using System.Text.Json;

namespace StreamPlatformBackend.Services.NotificationService
{
    public interface INotificationSender
    {
        Task SendToUserAsync(NotificationModel notification);
        Task NotifyStreamerSubscribersAsync(int streamerId, object payload, NotificationType type);
    }

    public class NotificationSender : INotificationSender
    {
        private readonly IHubContext<NotificationHub> _hub;
        private readonly IUserService _userService;
        private readonly INotificationRepository _notificationRepository;

        public NotificationSender(
            IHubContext<NotificationHub> hub,
            IUserService userService,
            INotificationRepository notificationRepository)
        {
            _hub = hub;
            _userService = userService;
            _notificationRepository = notificationRepository;
        }

        /// <summary>
        /// Отправляет уведомление одному пользователю
        /// </summary>
        public async Task SendToUserAsync(NotificationModel notification)
        {
            await _hub.Clients.Group($"user_{notification.UserId}")
                .SendAsync("ReceiveNotification", new
                {
                    Type = notification.Type.ToString().ToLower(),
                    Payload = notification.PayloadJson,
                    Date = notification.CreatedAt
                });
        }

        /// <summary>
        /// Рассылает уведомление всем подписчикам стримера
        /// </summary>
        public async Task NotifyStreamerSubscribersAsync(int streamerId,object payload,NotificationType type)
        {
            var subscribers = await _userService.GetSubscribersAsync(streamerId);

            foreach (var sub in subscribers)
            {
                var notification = new NotificationModel
                {
                    UserId = sub.Id,
                    Type = type,
                    PayloadJson = JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                };

                // сохранить в БД
                await _notificationRepository.CreateNotificationAsync(notification);

                // отправить по SignalR
                await SendToUserAsync(notification);
            }
        }
    }
}
