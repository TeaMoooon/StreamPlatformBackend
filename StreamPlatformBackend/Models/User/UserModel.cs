using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Stream;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace StreamPlatformBackend.Models.User
{
    public class UserModel
    {

        [Key]
        public int Id { get; set; }

        // ---------- Публичные данные ----------
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Nickname { get; set; } = string.Empty;

        [MaxLength(500)]
        public string ProfileDescription { get; set; } = string.Empty;

        public string ProfileImage { get; set; } = "/images/default-avatar.png";
        public string BackgroundImage { get; set; } = string.Empty;

        public int CashBalance { get; set; } = 0;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;

        public DateTime? LastOnlineDate { get; set; }

        public virtual ICollection<UserSocialLink> SocialLinks { get; set; } = new HashSet<UserSocialLink>();



        // ---------- Приватные/внутренние данные ----------
        [Required]
        public string Role { get; set; } = UserRole.User;

        [Required, JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        [JsonIgnore]
        public string StreamKey { get; set; } = string.Empty;

        public DateTime? LastAuthDate { get; set; }

        public bool EmailNotifications { get; set; } = false;
        public bool IsOnline { get; set; } = false;

        public string StreamServerUrl { get; set; } = "rtmp://your-server.com/live"; //под вопросом

        // ---------- Стрим данные ----------
        public string? LastPreviewUrl { get; set; }
        public string? LastStreamName { get; set; }
        public bool RecordEnabled { get; set; } = true;
        public List<string> LastTags { get; set; } = new();


        // ---------- Навигация ----------
        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscriptions { get; set; } = new HashSet<SubscriptionModel>();

        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscribers { get; set; } = new HashSet<SubscriptionModel>();

        public string StreamersLeague { get; set; } = StreamerLeagues.None;

        public int? CurrentStreamId { get; set; }
        [ForeignKey(nameof(CurrentStreamId))]
        public virtual StreamModel? CurrentStream { get; set; }

        [JsonIgnore]
        public virtual ICollection<StreamModel> StreamsHistory { get; set; } = new HashSet<StreamModel>();




        //public int? LastCategoryId { get; set; }


    }
}