using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    /// <summary>
    /// One person's signal on a report case. The case itself is <see cref="PlatformReport"/>.
    /// </summary>
    public class PlatformReportReporter
    {
        [Key]
        public long Id { get; set; }

        public long ReportId { get; set; }

        [ForeignKey(nameof(ReportId))]
        public virtual PlatformReport Report { get; set; } = null!;

        public int ReporterUserId { get; set; }

        [ForeignKey(nameof(ReporterUserId))]
        public virtual UserModel? Reporter { get; set; }

        [MaxLength(500)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
