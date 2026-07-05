using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamChatBanService
    {
        Task<bool> BanAsync(int streamerId, int bannedUserId, int bannedByUserId);
        Task<bool> UnbanAsync(int streamerId, int bannedUserId);
        Task<bool> IsBannedAsync(int streamerId, int userId);
        Task<List<int>> GetBannedUserIdsAsync(int streamerId);
        Task<List<StreamChatBanDto>> GetBannedUsersAsync(int streamerId);
        Task SyncAllToRedisAsync();
    }

    public class StreamChatBanService : IStreamChatBanService
    {
        private readonly AppDbContext _context;
        private readonly IRedisChatService _redisChatService;
        private readonly IStreamTeamService _streamTeamService;

        public StreamChatBanService(
            AppDbContext context,
            IRedisChatService redisChatService,
            IStreamTeamService streamTeamService)
        {
            _context = context;
            _redisChatService = redisChatService;
            _streamTeamService = streamTeamService;
        }

        public async Task<bool> BanAsync(int streamerId, int bannedUserId, int bannedByUserId)
        {
            var exists = await _context.StreamChatBans
                .AnyAsync(b => b.StreamerId == streamerId && b.BannedUserId == bannedUserId);

            if (!exists)
            {
                _context.StreamChatBans.Add(new StreamChatBan
                {
                    StreamerId = streamerId,
                    BannedUserId = bannedUserId,
                    BannedByUserId = bannedByUserId,
                    BannedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            await _redisChatService.SetBanAsync(streamerId, bannedUserId, true);
            await _streamTeamService.RemoveFromTeamAsync(streamerId, bannedUserId);
            return true;
        }

        public async Task<bool> UnbanAsync(int streamerId, int bannedUserId)
        {
            var entity = await _context.StreamChatBans
                .FirstOrDefaultAsync(b => b.StreamerId == streamerId && b.BannedUserId == bannedUserId);

            if (entity == null)
                return false;

            _context.StreamChatBans.Remove(entity);
            await _context.SaveChangesAsync();
            await _redisChatService.SetBanAsync(streamerId, bannedUserId, false);
            return true;
        }

        public Task<bool> IsBannedAsync(int streamerId, int userId)
            => _redisChatService.IsBannedAsync(streamerId, userId);

        public async Task<List<int>> GetBannedUserIdsAsync(int streamerId)
        {
            return await _context.StreamChatBans
                .Where(b => b.StreamerId == streamerId)
                .Select(b => b.BannedUserId)
                .ToListAsync();
        }

        public async Task<List<StreamChatBanDto>> GetBannedUsersAsync(int streamerId)
        {
            return await _context.StreamChatBans
                .AsNoTracking()
                .Where(b => b.StreamerId == streamerId)
                .OrderByDescending(b => b.BannedAt)
                .Select(b => new StreamChatBanDto
                {
                    UserId = b.BannedUserId,
                    Username = b.BannedUser.Nickname,
                    ProfileImage = b.BannedUser.ProfileImage,
                    BannedAt = b.BannedAt
                })
                .ToListAsync();
        }

        public async Task SyncAllToRedisAsync()
        {
            var bans = await _context.StreamChatBans
                .Select(b => new { b.StreamerId, b.BannedUserId })
                .ToListAsync();

            var grouped = bans.GroupBy(b => b.StreamerId);
            foreach (var group in grouped)
                await _redisChatService.SetBansAsync(group.Key, group.Select(b => b.BannedUserId));
        }
    }
}
