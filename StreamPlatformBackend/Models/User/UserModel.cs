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





        // Невидимые данные
        [Required]
        public string Role { get; set; } = UserRole.User;

        [Required]
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime? LastAuthDate { get; set; } = null;




        public bool EmailNotifications { get; set; } = false;


        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscriptions { get; set; } = new List<SubscriptionModel>();

        [JsonIgnore]
        public virtual ICollection<SubscriptionModel> Subscribers { get; set; } = new List<SubscriptionModel>();

        

        public string StreamersLeague { get; set; } = StreamerLeagues.None;



        /*Дополнительные полезные поля:

        DateTime? BirthDate — для проверки возраста.

        string Country / string TimeZoneId — для локализации и отображения времени.

        bool IsBanned и DateTime? BanExpires — для модерации.

        string? StripeCustomerId — для интеграции с платежной системой.

        // Для модерации
        public bool IsBanned { get; set; } = false;
        public DateTime? BanExpires { get; set; } = null;
        public string? BanReason { get; set; }
    
        // Для аналитики
        public int TotalViewCount { get; set; } = 0;
        public int FollowerCount { get; set; } = 0;
        
        // Для платежей
        public string? StripeCustomerId { get; set; }
        public string? PayPalEmail { get; set; }
        
        // Для локализации
        public string TimeZoneId { get; set; } = "UTC";
        public string Language { get; set; } = "en";
        
        // Для безопасности
        public bool TwoFactorEnabled { get; set; } = false;


        // Настройки
        public bool EmailNotifications { get; set; } = false;
        public bool IsEmailVerified { get; set; } = false; // Добавлено
        public string? EmailVerificationToken { get; set; } // Добавлено


            */





        public bool IsOnline { get; set; } = false;

        [JsonIgnore]
        public string StreamKey { get; set; } = string.Empty;

        public string StreamServerUrl { get; set; } = "rtmp://your-server.com/live"; //под вопросом

        public virtual StreamModel? CurrentStream { get; set; } = null;

        public string? LastPreviewlUrl { get; set; } //превью стрима
        public string? LastStreamName { get; set; }

        public int? LastCategoryId { get; set; }
        public string[] LastTags { get; set; } = Array.Empty<string>();


    }
}