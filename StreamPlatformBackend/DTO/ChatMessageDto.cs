using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;

namespace StreamPlatformBackend.DTO
{
    public class ChatMessageDto
    {
        public int Id { get; set; }
        public string UserNickname { get; set; } = string.Empty;
        public string UserProfileImage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public MessageType MessageType { get; set; }
        public decimal DonationAmount { get; set; }
        public string MessageColor { get; set; } = "#FFFFFF";
        public StreamerLeague UserLeague { get; set; }
    }
}