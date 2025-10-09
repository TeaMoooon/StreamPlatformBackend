namespace StreamPlatformBackend.DTO
{
    public class StreamDto
    {
        public int Id { get; set; }
        public string StreamName { get; set; } = string.Empty;
        public bool IsLive { get; set; }
        public string HlsUrl { get; set; } = string.Empty;
        public string UserNickname { get; set; } = string.Empty;
    }
}