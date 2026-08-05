namespace StreamPlatformBackend.DTO.UserDTO
{
    /// <summary>Public header-search hit — no email or staff fields.</summary>
    public class PublicUserSearchDto
    {
        public int Id { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public bool IsOnline { get; set; }
        public string? StreamName { get; set; }
    }
}
