using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    /// <summary>
    /// User appeal against a platform sanction (not channel chat bans).
    /// </summary>
    public class PlatformAppeal
    {
        [Key]
        public long Id { get; set; }

        public long SanctionId { get; set; }

        [ForeignKey(nameof(SanctionId))]
        public virtual PlatformSanction? Sanction { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel? User { get; set; }

        [Required, MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        [Required, MaxLength(16)]
        public string Status { get; set; } = AppealStatuses.Open;

        [MaxLength(500)]
        public string? StaffNote { get; set; }

        public int? ReviewedByUserId { get; set; }

        [ForeignKey(nameof(ReviewedByUserId))]
        public virtual UserModel? ReviewedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }
    }
}
