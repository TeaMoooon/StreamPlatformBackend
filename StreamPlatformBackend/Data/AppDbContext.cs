using Microsoft.EntityFrameworkCore;
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
        public DbSet<UserVideoLinkModel> UserVideoLinks { get; set; }
        public DbSet<StreamModel> Streams { get; set; }
        public DbSet<ChatMessageModel> ChatMessages { get; set; }
        public DbSet<ChatModeratorModel> ChatModerators { get; set; }
        public DbSet<BannedChatUserModel> BannedChatUsers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Уникальные индексы для UserModel
            modelBuilder.Entity<UserModel>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<UserModel>()
                .HasIndex(u => u.Nickname)
                .IsUnique();

            // Составной ключ для подписок (нельзя дважды подписаться на одного и того же)
            modelBuilder.Entity<SubscriptionModel>()
                .HasKey(s => new { s.SubscriberId, s.TargetUserId });

            // Проверка на самоподписку
            modelBuilder.Entity<SubscriptionModel>()
                .HasCheckConstraint("CK_Subscription_NotSelf", "[SubscriberId] != [TargetUserId]");

            // 🔗 ЯВНОЕ ОПИСАНИЕ ОТНОШЕНИЙ SubscriptionModel -> UserModel
            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.Subscriber)
                .WithMany() // или .WithMany(u => u.Subscriptions), если есть навигация в UserModel
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.TargetUser)
                .WithMany() // или .WithMany(u => u.Followers)
                .HasForeignKey(s => s.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }

    }
}