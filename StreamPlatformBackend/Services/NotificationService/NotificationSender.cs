using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using System.Text.Json;

namespace StreamPlatformBackend.Services.NotificationService
{
    public interface INotificationSender
    {
        Task SendToUserAsync(NotificationModel notification);
        Task NotifyStreamerSubscribersAsync(IEnumerable<UserModel> subscribers, int streamerId, object payload, NotificationType type);

    }

    public class NotificationSender : INotificationSender
    {
        private readonly IHubContext<NotificationHub> _hub;
        private readonly INotificationRepository _notificationRepository;

        public NotificationSender(IHubContext<NotificationHub> hub, INotificationRepository notificationRepository)
        {
            _hub = hub;
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
                    Id = notification.Id,
                    Type = notification.Type.ToString(),
                    Payload = notification.PayloadJson,
                    IsRead = notification.IsRead,
                    CreatedAt = notification.CreatedAt,
                    Date = notification.CreatedAt
                });
        }

        /// <summary>
        /// Рассылает уведомление всем подписчикам стримера
        /// </summary>
        public async Task NotifyStreamerSubscribersAsync(IEnumerable<UserModel> subscribers, int streamerId,object payload,NotificationType type)
        {

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
