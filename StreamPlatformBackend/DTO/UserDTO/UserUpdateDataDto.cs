using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserUpdateDataDto
    {
        [StringLength(50, MinimumLength = 5)]
        public string? Nickname { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ProfileDescription { get; set; } = string.Empty;

        public string? ProfileImage { get; set; } = string.Empty;

    }
}
