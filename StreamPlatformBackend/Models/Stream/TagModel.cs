using Azure;
using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.Models.Stream
{
    public class TagModel
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        // slug для поиска/сравнения (lowercase)
        [Required, MaxLength(50)]
        public string Slug { get; set; } = string.Empty;

        public virtual ICollection<StreamTagModel> Streams { get; set; } = new HashSet<StreamTagModel>();
    }

    public class StreamTagModel
    {
        public int StreamId { get; set; }
        public StreamModel Stream { get; set; } = null!;

        public int TagId { get; set; }
        public TagModel Tag { get; set; } = null!;
    }
}
