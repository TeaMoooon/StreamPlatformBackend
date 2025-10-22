using StreamPlatformBackend.DTO.StreamDTO;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class OnlineUserListDto
    {

        public string Nickname { get; set; } = string.Empty;

        public string ProfileImage { get; set; } = string.Empty;

        public bool IsOnline { get; set; }

        //public string ViewersCount { get; set; } = string.Empty;

        public string StreamersLeague { get; set; } = string.Empty;

        public string? PreviewlUrl { get; set; }

        public string? StreamName { get; set; }


    }
}
