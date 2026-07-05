using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamTeamService
    {
        Task<List<StreamTeamMemberDto>> GetTeamAsync(int streamerId);
        Task<StreamTeamAccessDto?> GetAccessAsync(int streamerId, int userId);
        Task<StreamTeamAccessDto?> GetAccessByNicknameAsync(string streamerNickname, int userId);
        Task<(bool Success, string? Error)> AddMemberAsync(int streamerId, int actorUserId, string memberNickname, string role);
        Task<(bool Success, string? Error)> RemoveMemberAsync(int streamerId, int actorUserId, int memberUserId);
        Task RemoveFromTeamAsync(int streamerId, int memberUserId);
        Task SyncAllToRedisAsync();
    }

    public class StreamTeamService : IStreamTeamService
    {
        private readonly AppDbContext _context;
        private readonly IRedisChatService _redisChatService;

        public StreamTeamService(AppDbContext context, IRedisChatService redisChatService)
        {
            _context = context;
            _redisChatService = redisChatService;
        }

        public async Task<List<StreamTeamMemberDto>> GetTeamAsync(int streamerId)
        {
            return await _context.StreamModerators
                .AsNoTracking()
                .Where(m => m.StreamerId == streamerId)
                .Include(m => m.Moderator)
                .OrderBy(m => m.Role)
                .ThenBy(m => m.Moderator.Nickname)
                .Select(m => new StreamTeamMemberDto
                {
                    UserId = m.ModeratorId,
                    Nickname = m.Moderator.Nickname,
                    ProfileImage = m.Moderator.ProfileImage,
                    Role = m.Role
                })
                .ToListAsync();
        }

        public async Task<StreamTeamAccessDto?> GetAccessAsync(int streamerId, int userId)
        {
            var streamer = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == streamerId);
            if (streamer == null)
                return null;

            return await BuildAccessAsync(streamer.Id, streamer.Nickname, userId);
        }

        public async Task<StreamTeamAccessDto?> GetAccessByNicknameAsync(string streamerNickname, int userId)
        {
            var streamer = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Nickname == streamerNickname);
            if (streamer == null)
                return null;

            return await BuildAccessAsync(streamer.Id, streamer.Nickname, userId);
        }

        public async Task<(bool Success, string? Error)> AddMemberAsync(
            int streamerId,
            int actorUserId,
            string memberNickname,
            string role)
        {
            if (!StreamTeamRoles.IsValid(role))
                return (false, "Некорректная роль");

            var normalizedNickname = memberNickname.Trim();
            if (string.IsNullOrWhiteSpace(normalizedNickname))
                return (false, "Укажите никнейм");

            var actorAccess = await BuildAccessAsync(streamerId, string.Empty, actorUserId);
            if (actorAccess == null)
                return (false, "Стример не найден");

            if (role == StreamTeamRoles.Moderator && !actorAccess.CanAssignModerators)
                return (false, "Недостаточно прав для назначения модератора");

            if (role == StreamTeamRoles.Assistant && !actorAccess.CanAssignAssistants)
                return (false, "Недостаточно прав для назначения ассистента");

            var member = await _context.Users.FirstOrDefaultAsync(u => u.Nickname == normalizedNickname);
            if (member == null)
                return (false, "Пользователь не найден");

            if (member.Id == streamerId)
                return (false, "Нельзя назначить стримера");

            if (member.Id == actorUserId)
                return (false, "Нельзя назначить себя");

            var existing = await _context.StreamModerators
                .FirstOrDefaultAsync(m => m.StreamerId == streamerId && m.ModeratorId == member.Id);

            if (existing != null)
            {
                if (existing.Role == role)
                    return (false, "Пользователь уже в команде с этой ролью");

                if (existing.Role == StreamTeamRoles.Moderator && role == StreamTeamRoles.Assistant)
                    return (false, "Сначала снимите пользователя с роли модератора");

                if (!actorAccess.CanAssignModerators && role == StreamTeamRoles.Moderator)
                    return (false, "Недостаточно прав");

                existing.Role = role;
            }
            else
            {
                _context.StreamModerators.Add(new StreamModerator
                {
                    StreamerId = streamerId,
                    ModeratorId = member.Id,
                    Role = role
                });
            }

            await _context.SaveChangesAsync();
            await SyncStreamerToRedisAsync(streamerId);
            return (true, null);
        }

        public async Task<(bool Success, string? Error)> RemoveMemberAsync(
            int streamerId,
            int actorUserId,
            int memberUserId)
        {
            var actorAccess = await BuildAccessAsync(streamerId, string.Empty, actorUserId);
            if (actorAccess == null || !actorAccess.CanManageTeam)
                return (false, "Недостаточно прав");

            var entity = await _context.StreamModerators
                .FirstOrDefaultAsync(m => m.StreamerId == streamerId && m.ModeratorId == memberUserId);

            if (entity == null)
                return (false, "Участник не найден");

            if (entity.Role == StreamTeamRoles.Moderator && !actorAccess.CanAssignModerators)
                return (false, "Только стример может снимать модераторов");

            _context.StreamModerators.Remove(entity);
            await _context.SaveChangesAsync();
            await SyncStreamerToRedisAsync(streamerId);
            return (true, null);
        }

        public async Task RemoveFromTeamAsync(int streamerId, int memberUserId)
        {
            var entity = await _context.StreamModerators
                .FirstOrDefaultAsync(m => m.StreamerId == streamerId && m.ModeratorId == memberUserId);

            if (entity == null)
                return;

            _context.StreamModerators.Remove(entity);
            await _context.SaveChangesAsync();
            await SyncStreamerToRedisAsync(streamerId);
        }

        public async Task SyncAllToRedisAsync()
        {
            var streamerIds = await _context.StreamModerators
                .Select(m => m.StreamerId)
                .Distinct()
                .ToListAsync();

            foreach (var streamerId in streamerIds)
                await SyncStreamerToRedisAsync(streamerId);
        }

        private async Task SyncStreamerToRedisAsync(int streamerId)
        {
            var members = await _context.StreamModerators
                .Where(m => m.StreamerId == streamerId)
                .ToListAsync();

            var moderators = members
                .Where(m => m.Role == StreamTeamRoles.Moderator)
                .Select(m => m.ModeratorId);

            var assistants = members
                .Where(m => m.Role == StreamTeamRoles.Assistant)
                .Select(m => m.ModeratorId);

            await _redisChatService.SetModeratorsAsync(streamerId, moderators);
            await _redisChatService.SetAssistantsAsync(streamerId, assistants);
        }

        private async Task<StreamTeamAccessDto?> BuildAccessAsync(int streamerId, string streamerNickname, int userId)
        {
            if (string.IsNullOrEmpty(streamerNickname))
            {
                streamerNickname = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == streamerId)
                    .Select(u => u.Nickname)
                    .FirstOrDefaultAsync() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(streamerNickname))
                return null;

            if (userId == streamerId)
            {
                return new StreamTeamAccessDto
                {
                    StreamerId = streamerId,
                    StreamerNickname = streamerNickname,
                    Role = "Streamer",
                    CanManageStream = true,
                    CanManageChat = true,
                    CanManageTeam = true,
                    CanAssignModerators = true,
                    CanAssignAssistants = true
                };
            }

            if (await _redisChatService.IsBannedAsync(streamerId, userId))
                return null;

            var membership = await _context.StreamModerators.AsNoTracking()
                .FirstOrDefaultAsync(m => m.StreamerId == streamerId && m.ModeratorId == userId);

            if (membership == null)
                return null;

            var isModerator = membership.Role == StreamTeamRoles.Moderator;
            var isAssistant = membership.Role == StreamTeamRoles.Assistant;

            return new StreamTeamAccessDto
            {
                StreamerId = streamerId,
                StreamerNickname = streamerNickname,
                Role = membership.Role,
                CanManageStream = isModerator,
                CanManageChat = isModerator || isAssistant,
                CanManageTeam = isModerator,
                CanAssignModerators = false,
                CanAssignAssistants = isModerator
            };
        }
    }
}
