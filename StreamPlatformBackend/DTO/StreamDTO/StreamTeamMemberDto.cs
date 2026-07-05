namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class StreamTeamMemberDto
    {
        public int UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public string Role { get; set; } = string.Empty;
    }

    public class AddStreamTeamMemberDto
    {
        public string Nickname { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class StreamTeamAccessDto
    {
        public int StreamerId { get; set; }
        public string StreamerNickname { get; set; } = string.Empty;
        public string? Role { get; set; }
        public bool CanManageStream { get; set; }
        public bool CanManageChat { get; set; }
        public bool CanManageTeam { get; set; }
        public bool CanAssignModerators { get; set; }
        public bool CanAssignAssistants { get; set; }
    }

    public class StreamChatBanDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? ProfileImage { get; set; }
        public DateTime BannedAt { get; set; }
    }
}
