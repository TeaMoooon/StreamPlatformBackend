using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    public class ChatModeratorModel
    {
        public int Id { get; set; }

        [Required]
        public int StreamId { get; set; }

        [Required]
        public int ModeratorId { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("StreamId")]
        public virtual StreamModel Stream { get; set; } = null!;

        [ForeignKey("ModeratorId")]
        public virtual UserModel Moderator { get; set; } = null!;
    }
}