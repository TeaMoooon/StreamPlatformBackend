namespace StreamPlatformBackend.DTO.StreamDTO
{
    /// <summary>
    /// Базовый DTO для обновления или запуска стрима
    /// </summary>
    public class StreamModifyDto
    {
        public string? StreamName { get; set; }
        public string[]? Tags { get; set; }
        public IFormFile? PreviewImage { get; set; }
        public int? CategoryId { get; set; }
    }

   
}
