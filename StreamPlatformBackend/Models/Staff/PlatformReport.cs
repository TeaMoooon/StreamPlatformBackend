using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    public class PlatformReport
    {
        [Key]
        public long Id { get; set; }

        public int ReporterUserId { get; set; }

        [ForeignKey(nameof(ReporterUserId))]
        public virtual UserModel? Reporter { get; set; }

        [Required, MaxLength(16)]
        public string TargetType { get; set; } = ReportTargetTypes.User;

        /// <summary>Primary subject of the report (user/channel owner).</summary>
        public int? TargetUserId { get; set; }

        [ForeignKey(nameof(TargetUserId))]
        public virtual UserModel? TargetUser { get; set; }

        [MaxLength(64)]
        public string? MessageId { get; set; }

        [MaxLength(1000)]
        public string? MessageSnapshot { get; set; }

        public int? StreamerId { get; set; }

        [ForeignKey(nameof(StreamerId))]
        public virtual UserModel? Streamer { get; set; }

        public int? StreamId { get; set; }

        [Required, MaxLength(32)]
        public string Reason { get; set; } = ReportReasons.Other;

        [MaxLength(500)]
        public string? Details { get; set; }

        [Required, MaxLength(16)]
        public string Status { get; set; } = ReportStatuses.New;

        public int? AssigneeUserId { get; set; }

        [ForeignKey(nameof(AssigneeUserId))]
        public virtual UserModel? Assignee { get; set; }

        [MaxLength(500)]
        public string? ResolutionNote { get; set; }

        public long? LinkedSanctionId { get; set; }

        [ForeignKey(nameof(LinkedSanctionId))]
        public virtual PlatformSanction? LinkedSanction { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }
    }
}
