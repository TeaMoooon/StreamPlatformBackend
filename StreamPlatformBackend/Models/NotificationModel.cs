using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models
{
    public class NotificationModel
    {
        public long Id { get; set; }

        /// <summary>
        /// Тип уведомления: stream_started, new_follower, donation, system, etc.
        /// </summary>
        public NotificationType Type { get; set; }

        /// <summary>
        /// Структурированные данные (например: StreamId, Amount, Username и т.д.)
        /// </summary>
        public string PayloadJson { get; set; } = null!;

        /// <summary>
        /// Пользователь-владелец уведомления
        /// </summary>
        public int UserId { get; set; }
        public UserModel User { get; set; } = null!;

        /// <summary>
        /// Было ли уведомление прочитано
        /// </summary>
        public bool IsRead { get; set; } = false;

        /// <summary>
        /// Когда создано (UTC)
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
