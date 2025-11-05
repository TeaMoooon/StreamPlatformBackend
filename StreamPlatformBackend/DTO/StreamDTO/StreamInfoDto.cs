namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class StreamInfoDto
    {
        public int StreamId { get; set; }
        public string StreamName { get; set; } = string.Empty;
        public string StreamerName { get; set; } = string.Empty;
        public int StreamerId { get; set; }
        //public string Category { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string? PreviewlUrl { get; set; }
        public string HlsUrl { get; set; } = string.Empty;
        public int TotalViews { get; set; }
        public DateTime? StartedAt { get; set; }
        public bool IsLive { get; set; }
    }
}
