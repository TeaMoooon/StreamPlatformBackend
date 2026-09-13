using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    [Table("StreamChatMessages")]
    public class StreamChatMessage
    {
        [Key]
        [MaxLength(32)]
        public string Id { get; set; } = string.Empty;

        [Required]
        public int StreamId { get; set; }

        [ForeignKey(nameof(StreamId))]
        public virtual StreamModel Stream { get; set; } = null!;

        [Required]
        public int StreamerId { get; set; }

        [ForeignKey(nameof(StreamerId))]
        public virtual UserModel Streamer { get; set; } = null!;

        [Required]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel User { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Text { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Role { get; set; } = "User";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public double OffsetSeconds { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAt { get; set; }

        public int? DeletedByUserId { get; set; }

        [ForeignKey(nameof(DeletedByUserId))]
        public virtual UserModel? DeletedByUser { get; set; }
    }
}
