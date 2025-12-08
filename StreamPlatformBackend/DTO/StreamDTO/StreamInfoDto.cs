namespace StreamPlatformBackend.DTO.StreamDTO
{
    /// <summary>
    /// Публичная информация о стриме
    /// </summary>
    public class StreamInfoDto
    {
        public int StreamId { get; set; }
        public string StreamName { get; set; } = string.Empty;
        public string StreamerName { get; set; } = string.Empty;
        public int StreamerId { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new(); // используем List<string>
        public string? PreviewUrl { get; set; } // исправлено
        public string HlsUrl { get; set; } = string.Empty;
        public int TotalViews { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsLive { get; set; }
    }
}
