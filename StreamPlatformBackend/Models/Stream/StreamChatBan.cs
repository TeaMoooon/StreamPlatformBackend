using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    [Table("StreamChatBans")]
    public class StreamChatBan
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StreamerId { get; set; }

        [ForeignKey(nameof(StreamerId))]
        public virtual UserModel Streamer { get; set; }

        [Required]
        public int BannedUserId { get; set; }

        [ForeignKey(nameof(BannedUserId))]
        public virtual UserModel BannedUser { get; set; }

        [Required]
        public int BannedByUserId { get; set; }

        [ForeignKey(nameof(BannedByUserId))]
        public virtual UserModel BannedByUser { get; set; }

        public DateTime BannedAt { get; set; } = DateTime.UtcNow;
    }
}
