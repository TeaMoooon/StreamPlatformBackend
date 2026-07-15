using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Staff;

namespace StreamPlatformBackend.Services
{
    public interface IStaffAuditService
    {
        Task WriteAsync(
            int actorUserId,
            string action,
            int? targetUserId = null,
            string? entityType = null,
            string? entityId = null,
            string? details = null);

        Task<List<StaffAuditLog>> GetRecentAsync(int take = 50);
    }

    public class StaffAuditService : IStaffAuditService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<StaffAuditService> _logger;

        public StaffAuditService(AppDbContext context, ILogger<StaffAuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task WriteAsync(
            int actorUserId,
            string action,
            int? targetUserId = null,
            string? entityType = null,
            string? entityId = null,
            string? details = null)
        {
            var entry = new StaffAuditLog
            {
                ActorUserId = actorUserId,
                TargetUserId = targetUserId,
                Action = action.Trim(),
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            };

            _context.StaffAuditLogs.Add(entry);
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Staff audit: actor={ActorUserId} action={Action} target={TargetUserId}",
                actorUserId,
                action,
                targetUserId);
        }

        public async Task<List<StaffAuditLog>> GetRecentAsync(int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            return await _context.StaffAuditLogs
                .AsNoTracking()
                .Include(l => l.Actor)
                .Include(l => l.TargetUser)
                .OrderByDescending(l => l.CreatedAt)
                .Take(take)
                .ToListAsync();
        }
    }
}
