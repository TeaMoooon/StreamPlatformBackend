using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    public class PlatformSanction
    {
        [Key]
        public long Id { get; set; }

        public int TargetUserId { get; set; }

        [ForeignKey(nameof(TargetUserId))]
        public virtual UserModel? TargetUser { get; set; }

        [Required, MaxLength(32)]
        public string Type { get; set; } = PlatformSanctionTypes.Warning;

        [Required, MaxLength(16)]
        public string Status { get; set; } = PlatformSanctionStatuses.Active;

        [Required, MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        public int IssuedByUserId { get; set; }

        [ForeignKey(nameof(IssuedByUserId))]
        public virtual UserModel? IssuedByUser { get; set; }

        public int? RevokedByUserId { get; set; }

        [ForeignKey(nameof(RevokedByUserId))]
        public virtual UserModel? RevokedByUser { get; set; }

        [MaxLength(500)]
        public string? RevokeReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Null means permanent.</summary>
        public DateTime? ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }
    }
}
