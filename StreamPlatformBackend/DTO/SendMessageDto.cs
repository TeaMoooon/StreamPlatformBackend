using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO
{
    public class SendMessageDto
    {
        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        public int StreamId { get; set; }
        public string? MessageColor { get; set; }
    }
}