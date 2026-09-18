using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public sealed class LoginHistoryPage
    {
        public required IReadOnlyList<UserLoginHistory> Items { get; init; }
        public int Total { get; init; }
        public int Skip { get; init; }
        public int Take { get; init; }
        public bool HasMore => Skip + Items.Count < Total;
    }

    public interface ILoginHistoryService
    {
        Task RecordSuccessfulLoginAsync(int userId, string ipAddress, string deviceType, CancellationToken ct = default);
        Task<LoginHistoryPage> GetForUserAsync(int userId, int skip = 0, int take = 5, CancellationToken ct = default);
    }

    public class LoginHistoryService : ILoginHistoryService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<LoginHistoryService> _logger;

        public LoginHistoryService(AppDbContext db, ILogger<LoginHistoryService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task RecordSuccessfulLoginAsync(int userId, string ipAddress, string deviceType, CancellationToken ct = default)
        {
            var entry = new UserLoginHistory
            {
                UserId = userId,
                LoggedInAt = DateTime.UtcNow,
                IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim(),
                DeviceType = string.IsNullOrWhiteSpace(deviceType) ? "Неизвестно" : deviceType.Trim()
            };

            _db.UserLoginHistories.Add(entry);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null)
                user.LastAuthDate = entry.LoggedInAt;

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Login history: user={UserId} ip={Ip} device={Device}",
                userId,
                entry.IpAddress,
                entry.DeviceType);
        }

        public async Task<LoginHistoryPage> GetForUserAsync(int userId, int skip = 0, int take = 5, CancellationToken ct = default)
        {
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 50);

            var query = _db.UserLoginHistories
                .AsNoTracking()
                .Where(h => h.UserId == userId);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(h => h.LoggedInAt)
                .ThenByDescending(h => h.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            return new LoginHistoryPage
            {
                Items = items,
                Total = total,
                Skip = skip,
                Take = take
            };
        }
    }
}
