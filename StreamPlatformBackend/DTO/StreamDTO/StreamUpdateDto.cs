namespace StreamPlatformBackend.DTO.StreamDTO
{
    public class StreamUpdateDto
    {
        public string? StreamName { get; set; }
        public int? CategoryId { get; set; }
        public string[]? Tags { get; set; }
        public string? PreviewlUrl { get; set; }
    }
}
