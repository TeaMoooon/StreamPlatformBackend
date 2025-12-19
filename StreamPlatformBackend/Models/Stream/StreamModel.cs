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
        public int UserId { get; set; }

        [JsonIgnore]
        public virtual UserModel User { get; set; } = null!;

        // ---------- Данные о стриме ----------
        [Required]
        public string StreamName { get; set; } = string.Empty;

        public virtual ICollection<StreamTagModel> Tags { get; set; } = new HashSet<StreamTagModel>();

        public string? PreviewUrl { get; set; }

        public int TotalViews { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public bool IsLive => StartedAt != null && EndedAt == null;

        // ---------- Категория ----------
        public int? CategoryId { get; set; } // nullable, чтобы старые стримы без категории не ломались

        [ForeignKey(nameof(CategoryId))]
        public virtual StreamCategoryModel? Category { get; set; }

        public bool RecordEnabled { get; set; } // по умолчанию записываем
        public string? RecordPath { get; set; }         // путь к mp4/mkv

        public DateTime? LastPingAt { get; set; }
        public bool WaitingReconnect { get; set; }

        //[Required] - временно
        [MaxLength(36)]
        public string PublicId { get; set; }//= null!; временно
    }
}

        // Можно добавить новые поля в будущем
        // public bool AllowClipCreation { get; set; } = true;
        // public bool IsChatEnabled { get; set; } = true;
        // public bool IsSubOnlyChat { get; set; } = false;





    // public int CategoryId { get; set; }
    //[ForeignKey("CategoryId")]
    //public virtual StreamCategory Category { get; set; } = null!;


    // Статистика просмотров

    // Временные метки

    //public bool AllowClipCreation { get; set; } = true; //может быть на будущее

    // public virtual ICollection<StreamChatMessageModel> ChatMessages { get; set; } //хз как чат этот делать

    /*
     * 
     * Вот надо подумать тут или не тут
    public bool IsChatEnabled { get; set; } = true;
    public bool IsSubOnlyChat { get; set; } = false;
    public int SlowModeInterval { get; set; } = 0;
    public bool EmoteOnlyMode { get; set; } = false;
    */
