namespace StreamPlatformBackend.DTO.StaffDTO
{
    public class StaffUserSearchDto
    {
        public int Id { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Role { get; set; } = "User";
        public DateTime CreatedAt { get; set; }
        public int ActiveSanctionCount { get; set; }
    }

    public class StaffUserDetailDto : StaffUserSearchDto
    {
        public List<StaffUserSanctionBriefDto> ActiveSanctions { get; set; } = new();
    }

    public class StaffUserSanctionBriefDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class SetStaffRoleDto
    {
        public string Role { get; set; } = string.Empty;
    }
}
