namespace StreamPlatformBackend.DTO.UserDTO
{
    public class AuthSessionDto
    {
        public long Id { get; set; }
        public string DeviceLabel { get; set; } = string.Empty;
        public string DeviceCategory { get; set; } = "Unknown";
        public DateTime CreatedAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        public bool IsCurrent { get; set; }
    }
}
