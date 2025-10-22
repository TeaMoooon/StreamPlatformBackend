namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class ListOnlineStreamDTO
    {
        public int Id { get; set; } // под вопросом 
        public string StreamTitle { get; set; }
        public string UserNickname { get; set; }
        public string UserCuttedAvatar { get; set; }
        public string Tags { get; set; }
        public int Viewers { get; set; }

    }
}
