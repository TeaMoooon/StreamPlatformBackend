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

        //Пользовательские данные
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Nickname { get; set; } = string.Empty;

        [MaxLength(500)]
        public string ProfileDescription { get; set; } = string.Empty;

        public string ProfileImage { get; set; } = "/images/default-avatar.png";

        public string BackgroundImage { get; set; } = string.Empty;

        public int CashBalance { get; set; } = 0;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;

        public DateTime? LastOnlineDate { get; set; } = null;

        public bool IsOnline { get; set; } = false;




        // Невидимые данные
        [Required]
        public string Role { get; set; } = UserRole.User;

        [Required]
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;
  
        public DateTime? LastAuthDate { get; set; } = null;




        public bool EmailNotifications { get; set; } = false;


        public int TotalSubscribers { get; set; } = 0;

        public int TotalPremiumSubscribers { get; set; } = 0;


        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscriptions { get; set; } = new List<SubscriptionModel>();

        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscribers { get; set; } = new List<SubscriptionModel>();

        public virtual ICollection<UserVideoLinkModel> VideoLinks { get; set; } = new List<UserVideoLinkModel>();

        public string StreamersLeague { get; set; } = StreamerLeagues.None;

        public virtual StreamModel? Stream { get; set; } = null;


        public bool IsStreamer { get; set; } = false;

        [JsonIgnore]
        public string StreamKey { get; set; } = string.Empty;

        public string StreamServerUrl { get; set; } = "rtmp://your-server.com/live";

    }
}