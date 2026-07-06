namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class StreamChatSettingsDto
    {
        public int SlowModeSeconds { get; set; }
        public string ChatRules { get; set; } = string.Empty;
    }

    public class UpdateStreamChatSettingsDto
    {
        public int? SlowModeSeconds { get; set; }
        public string? ChatRules { get; set; }
    }

    public class StreamChatModerationLogDto
    {
        public int Id { get; set; }
        public int ActorUserId { get; set; }
        public string ActorUsername { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public int? TargetUserId { get; set; }
        public string? TargetUsername { get; set; }
        public string? MessageId { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StreamDashboardSettingsDto
    {
        public string StreamName { get; set; } = string.Empty;
        public int? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public List<string> Tags { get; set; } = new();
        public string Language { get; set; } = "ru";
        public string Announcement { get; set; } = string.Empty;
        public bool IsLive { get; set; }
        public int SubscriberCount { get; set; }
        public DateTime? StartedAt { get; set; }
    }

    public class UpdateStreamDashboardSettingsDto
    {
        public string? StreamName { get; set; }
        public int? CategoryId { get; set; }
        public List<string>? Tags { get; set; }
        public string? Language { get; set; }
        public string? Announcement { get; set; }
    }
}
