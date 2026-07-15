namespace StreamPlatformBackend.DTO.StaffDTO
{
    public class IssuePlatformSanctionDto
    {
        public int TargetUserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        /// <summary>Duration in minutes. Null or &lt;= 0 = permanent.</summary>
        public int? DurationMinutes { get; set; }
    }

    public class RevokePlatformSanctionDto
    {
        public string? Reason { get; set; }
    }

    public class PlatformSanctionDto
    {
        public long Id { get; set; }
        public int TargetUserId { get; set; }
        public string? TargetNickname { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int IssuedByUserId { get; set; }
        public string? IssuedByNickname { get; set; }
        public int? RevokedByUserId { get; set; }
        public string? RevokedByNickname { get; set; }
        public string? RevokeReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
