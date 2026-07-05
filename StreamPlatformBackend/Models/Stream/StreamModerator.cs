using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    [Table("StreamModerators")]
    public class StreamModerator
    {
        [Key]
        public int Id { get; set; }

        // Стример, у которого есть модератор
        [Required]
        public int StreamerId { get; set; }

        [ForeignKey(nameof(StreamerId))]
        public virtual UserModel Streamer { get; set; }

        // Модератор
        [Required]
        public int ModeratorId { get; set; }

        [ForeignKey(nameof(ModeratorId))]
        public virtual UserModel Moderator { get; set; }

        [Required]
        [MaxLength(32)]
        public string Role { get; set; } = "Moderator";
    }
}
