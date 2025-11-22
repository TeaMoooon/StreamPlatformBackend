using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserLoginDto
    {
        [Required]
        public string LoginOrEmail { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

}
