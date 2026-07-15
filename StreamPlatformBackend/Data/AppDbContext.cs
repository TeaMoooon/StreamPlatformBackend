using Azure;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Staff;
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
        public DbSet<StreamChatBan> StreamChatBans { get; set; }
        public DbSet<StreamChatModerationLog> StreamChatModerationLogs { get; set; }
        public DbSet<StaffAuditLog> StaffAuditLogs { get; set; }
        public DbSet<PlatformSanction> PlatformSanctions { get; set; }
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
                .Property(s => s.IsActive)
                .HasDefaultValue(true);

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

            modelBuilder.Entity<StreamModerator>()
                .HasIndex(sm => new { sm.StreamerId, sm.ModeratorId })
                .IsUnique();

            modelBuilder.Entity<StreamChatBan>()
                .HasIndex(b => new { b.StreamerId, b.BannedUserId })
                .IsUnique();

            modelBuilder.Entity<StreamChatBan>()
                .HasOne(b => b.Streamer)
                .WithMany()
                .HasForeignKey(b => b.StreamerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StreamChatBan>()
                .HasOne(b => b.BannedUser)
                .WithMany()
                .HasForeignKey(b => b.BannedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StreamChatBan>()
                .HasOne(b => b.BannedByUser)
                .WithMany()
                .HasForeignKey(b => b.BannedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StreamChatModerationLog>()
                .HasOne(l => l.Streamer)
                .WithMany()
                .HasForeignKey(l => l.StreamerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StreamChatModerationLog>()
                .HasOne(l => l.Actor)
                .WithMany()
                .HasForeignKey(l => l.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StreamChatModerationLog>()
                .HasIndex(l => new { l.StreamerId, l.CreatedAt });

            modelBuilder.Entity<StaffAuditLog>()
                .HasOne(l => l.Actor)
                .WithMany()
                .HasForeignKey(l => l.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StaffAuditLog>()
                .HasOne(l => l.TargetUser)
                .WithMany()
                .HasForeignKey(l => l.TargetUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<StaffAuditLog>()
                .HasIndex(l => l.CreatedAt);

            modelBuilder.Entity<StaffAuditLog>()
                .HasIndex(l => new { l.ActorUserId, l.CreatedAt });

            modelBuilder.Entity<PlatformSanction>()
                .HasOne(s => s.TargetUser)
                .WithMany()
                .HasForeignKey(s => s.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlatformSanction>()
                .HasOne(s => s.IssuedByUser)
                .WithMany()
                .HasForeignKey(s => s.IssuedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PlatformSanction>()
                .HasOne(s => s.RevokedByUser)
                .WithMany()
                .HasForeignKey(s => s.RevokedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PlatformSanction>()
                .HasIndex(s => new { s.TargetUserId, s.Status });

            modelBuilder.Entity<PlatformSanction>()
                .HasIndex(s => s.CreatedAt);

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

            
            modelBuilder.Entity<StreamModel>()
                .HasIndex(x => x.PublicId)
                .IsUnique();
        }

    }
}