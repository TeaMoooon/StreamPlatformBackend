using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models;

namespace StreamPlatformBackend.Services.NotificationService
{
    public interface INotificationRepository
    {
        Task<NotificationModel> CreateNotificationAsync(NotificationModel notification);
        Task<IEnumerable<NotificationModel>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 25);
        Task MarkAllAsReadAsync(int userId);
        Task MarkAsReadAsync(int userId, long[] notificationIds);
    }

    public class NotificationRepository : INotificationRepository
    {
        private readonly AppDbContext _context;

        public NotificationRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<NotificationModel> CreateNotificationAsync(NotificationModel notification)
        {
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
            return notification;
        }

        public async Task<IEnumerable<NotificationModel>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 25)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in notifications)
                n.IsRead = true;

            await _context.SaveChangesAsync();
        }

        public async Task MarkAsReadAsync(int userId, long[] notificationIds)
        {
            var notifications = await _context.Notifications
                .Where(n => notificationIds.Contains(n.Id) && n.UserId == userId)
                .ToListAsync();

            foreach (var n in notifications)
                n.IsRead = true;

            await _context.SaveChangesAsync();
        }
    }
}
