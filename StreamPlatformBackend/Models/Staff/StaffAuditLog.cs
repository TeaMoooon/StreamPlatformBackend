using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    public class StaffAuditLog
    {
        [Key]
        public long Id { get; set; }

        public int ActorUserId { get; set; }

        [ForeignKey(nameof(ActorUserId))]
        public virtual UserModel? Actor { get; set; }

        public int? TargetUserId { get; set; }

        [ForeignKey(nameof(TargetUserId))]
        public virtual UserModel? TargetUser { get; set; }

        [Required, MaxLength(64)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? EntityType { get; set; }

        [MaxLength(64)]
        public string? EntityId { get; set; }

        [MaxLength(1000)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
