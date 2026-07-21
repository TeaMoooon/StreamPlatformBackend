namespace StreamPlatformBackend.DTO.StaffDTO
{
    public class CreateSupportTicketDto
    {
        public string Category { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class AddSupportTicketMessageDto
    {
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateSupportTicketDto
    {
        public string? Status { get; set; }
        public bool AssignToMe { get; set; }
        public bool EscalateToReport { get; set; }
        public string? EscalateReason { get; set; }
        public string? EscalateDetails { get; set; }
    }

    public class SupportTicketMessageDto
    {
        public long Id { get; set; }
        public int AuthorUserId { get; set; }
        public string? AuthorNickname { get; set; }
        public bool IsStaff { get; set; }
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class SupportTicketDto
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string? UserNickname { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int? AssigneeUserId { get; set; }
        public string? AssigneeNickname { get; set; }
        public long? EscalatedReportId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public List<SupportTicketMessageDto> Messages { get; set; } = new();
    }
}
