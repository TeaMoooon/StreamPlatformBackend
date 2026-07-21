namespace StreamPlatformBackend.DTO.StaffDTO
{
    public class CreatePlatformAppealDto
    {
        public long SanctionId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UpdatePlatformAppealDto
    {
        public string? Status { get; set; }
        public string? StaffNote { get; set; }
        /// <summary>When approving, revoke the linked sanction.</summary>
        public bool RevokeSanction { get; set; } = true;
    }

    public class PlatformAppealDto
    {
        public long Id { get; set; }
        public long SanctionId { get; set; }
        public string SanctionType { get; set; } = string.Empty;
        public string SanctionStatus { get; set; } = string.Empty;
        public string SanctionReason { get; set; } = string.Empty;
        public DateTime? SanctionExpiresAt { get; set; }
        public int UserId { get; set; }
        public string? UserNickname { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? StaffNote { get; set; }
        public int? ReviewedByUserId { get; set; }
        public string? ReviewedByNickname { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    public class StaffQueueStatsDto
    {
        public int ReportsNew { get; set; }
        public int ReportsInProgress { get; set; }
        public int TicketsOpen { get; set; }
        public int TicketsInProgress { get; set; }
        public int TicketsWaitingUser { get; set; }
        public int AppealsOpen { get; set; }
        public int ActiveSanctions { get; set; }
        public int StaleReports { get; set; }
        public int StaleTickets { get; set; }
    }
}
