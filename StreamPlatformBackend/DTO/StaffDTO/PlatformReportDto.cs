namespace StreamPlatformBackend.DTO.StaffDTO
{
    public class CreatePlatformReportDto
    {
        public string TargetType { get; set; } = string.Empty;
        public int? TargetUserId { get; set; }
        public string? MessageId { get; set; }
        public string? MessageSnapshot { get; set; }
        public int? StreamerId { get; set; }
        public int? StreamId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    /// <summary>Public ack for POST /api/reports — never includes co-reporters or staff fields.</summary>
    public class CreatePlatformReportAckDto
    {
        public long Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ReporterCount { get; set; }
    }

    public class UpdatePlatformReportDto
    {
        public string? Status { get; set; }
        public string? ResolutionNote { get; set; }
        public bool AssignToMe { get; set; }
        /// <summary>Optional: issue sanction when resolving.</summary>
        public IssuePlatformSanctionDto? Sanction { get; set; }
    }

    public class PlatformReportReporterDto
    {
        public long Id { get; set; }
        public int ReporterUserId { get; set; }
        public string? ReporterNickname { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PlatformReportDto
    {
        public long Id { get; set; }
        /// <summary>First reporter (case opener). Prefer <see cref="Reporters"/> for the full list.</summary>
        public int ReporterUserId { get; set; }
        public string? ReporterNickname { get; set; }
        public int ReporterCount { get; set; }
        public List<PlatformReportReporterDto> Reporters { get; set; } = new();
        public string TargetType { get; set; } = string.Empty;
        public int? TargetUserId { get; set; }
        public string? TargetNickname { get; set; }
        public string? MessageId { get; set; }
        public string? MessageSnapshot { get; set; }
        public int? StreamerId { get; set; }
        public string? StreamerNickname { get; set; }
        public int? StreamId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? AssigneeUserId { get; set; }
        public string? AssigneeNickname { get; set; }
        public string? ResolutionNote { get; set; }
        public long? LinkedSanctionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
