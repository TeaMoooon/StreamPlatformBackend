namespace StreamPlatformBackend.DTO.UserDTO
{
    public class UserLoginHistoryDto
    {
        public long Id { get; set; }
        public DateTime LoggedInAt { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
    }
}
