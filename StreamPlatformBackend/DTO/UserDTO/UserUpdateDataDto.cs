using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class SocialLinkDto
    {
        [Required]
        [MaxLength(50)]
        public string Platform { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Url { get; set; } = string.Empty;
    }

    public class UserUpdateDataDto
    {
        public string? Nickname { get; set; }
        public string? Email { get; set; }
        public string? ProfileDescription { get; set; }

        // 🔹 для смены пароля
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }

        public List<SocialLinkDto>? SocialLinks { get; set; }  // соцсети

        public bool RecordEnabled { get; set; }

        public IFormFile? ProfileImage { get; set; }
        public IFormFile? BackgroundImage { get; set; }

    }
}
