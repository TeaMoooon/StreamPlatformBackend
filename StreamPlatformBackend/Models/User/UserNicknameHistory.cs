using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StreamPlatformBackend.Models.User
{
    /// <summary>
    /// Служебная история никнеймов пользователя (аудит идентификации).
    /// Не отдаётся в публичный API — только для внутренних запросов.
    /// </summary>
    public class UserNicknameHistory
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel? User { get; set; }

        [Required, MaxLength(50)]
        public string Nickname { get; set; } = string.Empty;

        /// <summary>Когда никнейм был установлен (UTC).</summary>
        public DateTime SetAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Когда никнейм перестал действовать (UTC).
        /// null — текущий никнейм пользователя.
        /// </summary>
        public DateTime? EndedAt { get; set; }
    }
}
