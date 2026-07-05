namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class ChatMessageDto
    {
        public string Id { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Text { get; set; }
        public string Role { get; set; }  // User / Moderator / Admin / Streamer
        public DateTime Timestamp { get; set; } // для записи в БД
        public double OffsetSeconds { get; set; } // для синхронизации с VOD
        public bool IsDeleted { get; set; }
    }
}
