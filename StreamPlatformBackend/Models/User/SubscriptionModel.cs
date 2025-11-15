using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace StreamPlatformBackend.Models.User
{
    public class SubscriptionModel
    {
        public int SubscriberId { get; set; }
        public int TargetUserId { get; set; }

        public DateTime SubscriptionDate { get; set; } = DateTime.UtcNow;

        [JsonIgnore, ForeignKey(nameof(SubscriberId))]
        public virtual UserModel Subscriber { get; set; } = null!;

        [JsonIgnore, ForeignKey(nameof(TargetUserId))]
        public virtual UserModel TargetUser { get; set; } = null!;
    }
}