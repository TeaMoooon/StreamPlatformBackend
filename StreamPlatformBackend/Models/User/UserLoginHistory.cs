using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.User
{
    /// <summary>Успешный вход пользователя (аудит).</summary>
    public class UserLoginHistory
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel? User { get; set; }

        /// <summary>Момент входа (UTC).</summary>
        public DateTime LoggedInAt { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(64)]
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>Метка устройства, напр. «Chrome · Windows» (раньше: Desktop/Mobile/Tablet).</summary>
        [Required, MaxLength(96)]
        public string DeviceType { get; set; } = "Неизвестно";
    }
}
