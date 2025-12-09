using Azure;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<UserModel> Users { get; set; }
        public DbSet<SubscriptionModel> Subscriptions { get; set; }
        public DbSet<StreamModel> Streams { get; set; }
        public DbSet<StreamCategoryModel> StreamCategories { get; set; }
        public DbSet<NotificationModel> Notifications { get; set; }
        public DbSet<UserSocialLink> UserSocialLinks { get; set; }
        public DbSet<StreamModerator> StreamModerators { get; set; }
        public DbSet<TagModel> Tags { get; set; }
        public DbSet<StreamTagModel> StreamTags { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Составной ключ для подписок (нельзя дважды подписаться на одного и того же)
            modelBuilder.Entity<SubscriptionModel>().HasKey(s => new { s.SubscriberId, s.TargetUserId });

            modelBuilder.Entity<SubscriptionModel>()
                .ToTable(t => t.HasCheckConstraint("CK_Subscription_NotSelf", "\"SubscriberId\" != \"TargetUserId\""));

            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.Subscriber)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.TargetUser)
                .WithMany(u => u.Subscribers)
                .HasForeignKey(s => s.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StreamModel>()
                .HasOne(s => s.Category)
                .WithMany(c => c.Streams)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<NotificationModel>()
                .HasIndex(n => new { n.UserId, n.CreatedAt });

            modelBuilder.Entity<UserSocialLink>()
                .HasOne(s => s.User)
                .WithMany(u => u.SocialLinks)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // История стримов: User -> StreamsHistory (1:N)
            modelBuilder.Entity<StreamModel>()
                .HasOne(s => s.User)
                .WithMany(u => u.StreamsHistory)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Текущий стрим: User -> CurrentStream (1:1), через CurrentStreamId
            modelBuilder.Entity<UserModel>()
                .HasOne(u => u.CurrentStream)
                .WithMany() // нет обратной навигации
                .HasForeignKey(u => u.CurrentStreamId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<StreamModerator>()
                .HasOne(sm => sm.Streamer)
                .WithMany(u => u.Moderators)
                .HasForeignKey(sm => sm.StreamerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StreamModerator>()
                .HasOne(sm => sm.Moderator)
                .WithMany(u => u.ModeratedStreams)
                .HasForeignKey(sm => sm.ModeratorId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StreamTagModel>()
                .HasKey(st => new { st.StreamId, st.TagId });

            modelBuilder.Entity<StreamTagModel>()
                .HasOne(st => st.Stream)
                .WithMany(s => s.Tags)
                .HasForeignKey(st => st.StreamId);

            modelBuilder.Entity<StreamTagModel>()
                .HasOne(st => st.Tag)
                .WithMany(t => t.Streams)
                .HasForeignKey(st => st.TagId);

            modelBuilder.Entity<TagModel>()
                .HasIndex(t => t.Slug)
                .IsUnique();
        }

    }
}