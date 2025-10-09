using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace StreamPlatformBackend.Models.Stream
{
    public class StreamModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string StreamName { get; set; } = string.Empty;

        [Required]
        public string StreamKey { get; set; } = string.Empty;

        public bool IsLive { get; set; }

        [Url]
        public string HlsUrl { get; set; } = string.Empty;

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual UserModel User { get; set; } = null!;

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        [JsonIgnore]
        public virtual ICollection<ChatMessageModel> ChatMessages { get; set; } = new List<ChatMessageModel>();

        [JsonIgnore]
        public virtual ICollection<ChatModeratorModel> Moderators { get; set; } = new List<ChatModeratorModel>();

        [JsonIgnore]
        public virtual ICollection<BannedChatUserModel> BannedUsers { get; set; } = new List<BannedChatUserModel>();

        public bool IsChatEnabled { get; set; } = true;
        public bool IsSubOnlyChat { get; set; } = false;
        public int SlowModeInterval { get; set; } = 0;
        public bool EmoteOnlyMode { get; set; } = false;
    }
}