using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Models.Staff
{
    public class SupportTicket
    {
        [Key]
        public long Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserModel? User { get; set; }

        [Required, MaxLength(32)]
        public string Category { get; set; } = TicketCategories.Other;

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(16)]
        public string Status { get; set; } = TicketStatuses.Open;

        public int? AssigneeUserId { get; set; }

        [ForeignKey(nameof(AssigneeUserId))]
        public virtual UserModel? Assignee { get; set; }

        public long? EscalatedReportId { get; set; }

        [ForeignKey(nameof(EscalatedReportId))]
        public virtual PlatformReport? EscalatedReport { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }

        public virtual ICollection<SupportTicketMessage> Messages { get; set; } = new HashSet<SupportTicketMessage>();
    }

    public class SupportTicketMessage
    {
        [Key]
        public long Id { get; set; }

        public long TicketId { get; set; }

        [ForeignKey(nameof(TicketId))]
        public virtual SupportTicket? Ticket { get; set; }

        public int AuthorUserId { get; set; }

        [ForeignKey(nameof(AuthorUserId))]
        public virtual UserModel? Author { get; set; }

        public bool IsStaff { get; set; }

        [Required, MaxLength(4000)]
        public string Body { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
