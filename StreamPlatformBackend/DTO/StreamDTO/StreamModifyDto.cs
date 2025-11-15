namespace StreamPlatformBackend.DTO.StreamDTO
{
    /// <summary>
    /// Базовый DTO для обновления или запуска стрима
    /// </summary>
    public class StreamModifyDto
    {
        public string? StreamName { get; set; }
        public List<string>? Tags { get; set; }
        public string? PreviewUrl { get; set; }
    }

   
}
