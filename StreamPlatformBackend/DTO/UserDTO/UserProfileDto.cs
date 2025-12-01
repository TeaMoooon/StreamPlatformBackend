using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserProfileDto
    {
        public int Id { get; set; }

        public string Email { get; set; } = string.Empty;

        public string Nickname { get; set; } = string.Empty;

        public string ProfileDescription { get; set; } = string.Empty;

        public string BackgroundImage { get; set; } = string.Empty;

        public string ProfileImage { get; set; } = string.Empty;

        public DateTime RegistrationDate { get; set; }

        public bool IsOnline { get; set; }

        public int CashBalance { get; set; } = 0;

        public ICollection<UserSocialLinkDto> SocialLinks { get; set; } = new List<UserSocialLinkDto>();

    }
}