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

        // Исправляем опечатку в имени поля
        public string? PreviewUrl { get; set; }

        public string? StreamName { get; set; }

        // Опционально: ID стрима, если понадобится на фронтенде
        public int? StreamId { get; set; }


    }
}
