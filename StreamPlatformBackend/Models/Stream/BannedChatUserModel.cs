using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    public class BannedChatUserModel
    {
        public int Id { get; set; }

        [Required]
        public int StreamId { get; set; }

        [Required]
        public int BannedUserId { get; set; }

        public DateTime BannedAt { get; set; } = DateTime.UtcNow;

        public DateTime? BannedUntil { get; set; }

        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        [ForeignKey("StreamId")]
        public virtual StreamModel Stream { get; set; } = null!;

        [ForeignKey("BannedUserId")]
        public virtual UserModel BannedUser { get; set; } = null!;
    }
}