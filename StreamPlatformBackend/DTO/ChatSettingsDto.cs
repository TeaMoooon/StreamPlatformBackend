namespace StreamPlatformBackend.DTO
{
    public class ChatSettingsDto
    {
        public bool IsChatEnabled { get; set; }
        public bool IsSubOnlyChat { get; set; }
        public int SlowModeInterval { get; set; }
        public bool EmoteOnlyMode { get; set; }
    }
}