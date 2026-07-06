using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    [Table("StreamChatModerationLogs")]
    public class StreamChatModerationLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StreamerId { get; set; }

        [ForeignKey(nameof(StreamerId))]
        public virtual UserModel Streamer { get; set; } = null!;

        [Required]
        public int ActorUserId { get; set; }

        [ForeignKey(nameof(ActorUserId))]
        public virtual UserModel Actor { get; set; } = null!;

        [Required]
        [MaxLength(32)]
        public string Action { get; set; } = string.Empty;

        public int? TargetUserId { get; set; }

        [MaxLength(50)]
        public string? TargetUsername { get; set; }

        [MaxLength(64)]
        public string? MessageId { get; set; }

        [MaxLength(500)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
