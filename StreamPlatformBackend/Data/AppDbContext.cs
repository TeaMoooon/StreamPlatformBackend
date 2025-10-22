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
        public DbSet<StreamModel> Streams { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Составной ключ для подписок (нельзя дважды подписаться на одного и того же)
            modelBuilder.Entity<SubscriptionModel>().HasKey(s => new { s.SubscriberId, s.TargetUserId });


            modelBuilder.Entity<SubscriptionModel>()
                .ToTable(t => t.HasCheckConstraint("CK_Subscription_NotSelf","\"SubscriberId\" != \"TargetUserId\""));

            // 🔥 ОБНОВЛЕННЫЕ ОТНОШЕНИЯ - используйте навигационные свойства
            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.Subscriber)
                .WithMany(u => u.Subscriptions) // Ссылаемся на навигационное свойство
                .HasForeignKey(s => s.SubscriberId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SubscriptionModel>()
                .HasOne(s => s.TargetUser)
                .WithMany(u => u.Subscribers) // Ссылаемся на навигационное свойство
                .HasForeignKey(s => s.TargetUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SubscriptionModel>().HasKey(s => new { s.SubscriberId, s.TargetUserId });
        }
    }
}