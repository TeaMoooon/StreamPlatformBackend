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
        public virtual UserModel User { get; set; }

        // Данные о текущем стриме
        public string StreamName { get; set; } = string.Empty;

        // public int CategoryId { get; set; }
        //[ForeignKey("CategoryId")]
        //public virtual StreamCategory Category { get; set; } = null!;

        public string[] Tags { get; set; } = Array.Empty<string>();

        public string? PreviewlUrl { get; set; }

        // Статистика просмотров
        public int TotalViews { get; set; } = 0;

        // Временные метки
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

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
        

    }
}
