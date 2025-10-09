using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.Stream
{
    public class ChatMessageModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int StreamId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; } = false;

        public MessageType MessageType { get; set; } = MessageType.Regular;

        public decimal DonationAmount { get; set; } = 0;

        [MaxLength(20)]
        public string MessageColor { get; set; } = "#FFFFFF";

        [ForeignKey("UserId")]
        public virtual UserModel User { get; set; } = null!;

        [ForeignKey("StreamId")]
        public virtual StreamModel Stream { get; set; } = null!;
    }
}