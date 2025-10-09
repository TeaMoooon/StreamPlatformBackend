using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.User
{
    public class UserVideoLinkModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [Url]
        [MaxLength(500)]
        public string VideoUrl { get; set; } = string.Empty;

        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual UserModel User { get; set; } = null!;
    }
}