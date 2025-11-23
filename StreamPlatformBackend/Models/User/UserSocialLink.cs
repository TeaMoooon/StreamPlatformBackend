using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.User
{
    public class UserSocialLink
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("User")]
        public int UserId { get; set; }

        public UserModel User { get; set; }

        [Required]
        [MaxLength(50)]
        public string Platform { get; set; }  // youtube, twitch, telegram и т.д.

        [Required]
        [MaxLength(200)]
        public string Url { get; set; }
    }
}
