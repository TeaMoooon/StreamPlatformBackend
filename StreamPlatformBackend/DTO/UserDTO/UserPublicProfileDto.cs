using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserPublicProfileDto
    {
        public int Id { get; set; }

        public string Nickname { get; set; } = string.Empty;

        public string ProfileDescription { get; set; } = string.Empty;

        public string BackgroundImage { get; set; } = string.Empty;

        public string ProfileImage { get; set; } = string.Empty;

        public DateTime RegistrationDate { get; set; }

        public bool IsOnline { get; set; }

        public virtual StreamModel? CurrentStream { get; set; }

        public ICollection<UserSocialLinkDto> SocialLinks { get; set; } = new List<UserSocialLinkDto>();


    }
}
