using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserUpdateDataDto
    {
       public string? Nickname { get; set; }

        [EmailAddress]
        public string? Email { get; set; }     // необязательное поле

        [MaxLength(500)]
        public string? ProfileDescription { get; set; }

        public string? ProfileImage { get; set; }

        public string? BackgroundImage { get; set; }

    }
}
