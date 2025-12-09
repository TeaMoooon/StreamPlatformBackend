    using System.ComponentModel.DataAnnotations;

    namespace StreamPlatformBackend.Models.Stream
    {
        public class StreamCategoryModel
        {
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(500)]
            public string Description { get; set; } = string.Empty;

            public string BannerImageUrl { get; set; } = string.Empty;


            //Подключить редиску
            /*
            public int TotalViewers { get; set; }
            public int TotalStreams { get; set; } // Количество активных стримов
            */
            public virtual ICollection<StreamModel> Streams { get; set; } = new List<StreamModel>();
        }
    }
