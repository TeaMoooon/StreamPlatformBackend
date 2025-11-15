using StreamPlatformBackend.Models.User;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

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

        public List<string> Tags { get; set; } = new();

        public string? PreviewUrl { get; set; }

        public int TotalViews { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public bool IsActive => StartedAt != null && EndedAt == null;

        // ---------- Категория ----------
        public int? CategoryId { get; set; } // nullable, чтобы старые стримы без категории не ломались

        [ForeignKey(nameof(CategoryId))]
        public virtual StreamCategory? Category { get; set; }
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
