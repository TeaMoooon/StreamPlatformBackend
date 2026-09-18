using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.User
{
    public class RefreshToken
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel? User { get; set; }

        /// <summary>SHA-256 hex of the opaque refresh token (never store raw).</summary>
        [Required, MaxLength(64)]
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>Стабильный id цепочки ротаций одной «сессии устройства».</summary>
        public Guid SessionFamilyId { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        [MaxLength(64)]
        public string? ReplacedByTokenHash { get; set; }

        [MaxLength(64)]
        public string IpAddress { get; set; } = "unknown";

        /// <summary>Напр. «Chrome · Windows».</summary>
        [MaxLength(96)]
        public string DeviceLabel { get; set; } = "Неизвестно";

        /// <summary>Desktop | Mobile | Tablet | Unknown</summary>
        [MaxLength(16)]
        public string DeviceCategory { get; set; } = "Unknown";

        public bool IsActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    }
}
