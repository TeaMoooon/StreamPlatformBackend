    using System.ComponentModel.DataAnnotations;

    namespace StreamPlatformBackend.Models.Stream
    {
        public class StreamCategoryModel
        {
            public int Id { get; set; }

            [Required]
            [MaxLength(100)]
            public string? Name { get; set; }

            public string? Type { get; set; }

            public string[]? Tags { get; set; }



            [MaxLength(500)]
            public string? Description { get; set; }

            public string? BannerImageUrl { get; set; }

            [Required]
            [MaxLength(150)]
            public string Slug { get; set; } = null!;


        //Подключить редиску
        /*
        public int TotalViewers { get; set; }
        public int TotalStreams { get; set; } // Количество активных стримов
        */
            public virtual ICollection<StreamModel> Streams { get; set; } = new List<StreamModel>();
        }
    }
