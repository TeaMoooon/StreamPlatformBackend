namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserSocialLinkDto
    {
        public int Id { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
