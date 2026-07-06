using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/streamers")]
    public class StreamChatBansController : ControllerBase
    {
        private readonly IStreamChatBanService _streamChatBanService;
        private readonly IStreamTeamService _streamTeamService;
        private readonly IUserService _userService;
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly IStreamChatModerationLogService _moderationLogService;
        private readonly ILogger<StreamChatBansController> _logger;

        public StreamChatBansController(
            IStreamChatBanService streamChatBanService,
            IStreamTeamService streamTeamService,
            IUserService userService,
            IHubContext<StreamHub> hubContext,
            IStreamChatModerationLogService moderationLogService,
            ILogger<StreamChatBansController> logger)
        {
            _streamChatBanService = streamChatBanService;
            _streamTeamService = streamTeamService;
            _userService = userService;
            _hubContext = hubContext;
            _moderationLogService = moderationLogService;
            _logger = logger;
        }

        [HttpGet("{streamerId:int}/bans")]
        public async Task<IActionResult> GetBans(int streamerId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var access = await _streamTeamService.GetAccessAsync(streamerId, userId);
                if (access == null || !access.CanManageChat)
                    return Forbid();

                var bans = await _streamChatBanService.GetBannedUsersAsync(streamerId);
                return Ok(new { bans });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bans for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить список банов" });
            }
        }

        [HttpDelete("{streamerId:int}/bans/{bannedUserId:int}")]
        public async Task<IActionResult> Unban(int streamerId, int bannedUserId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var access = await _streamTeamService.GetAccessAsync(streamerId, userId);
                if (access == null || !access.CanManageChat)
                    return Forbid();

                var unbanned = await _streamChatBanService.UnbanAsync(streamerId, bannedUserId);
                if (!unbanned)
                    return NotFound(new { message = "Пользователь не найден в списке банов" });

                var user = await _userService.GetUserByIdAsync(bannedUserId);
                await _hubContext.Clients.Group($"stream_{streamerId}")
                    .SendAsync("ChatUserUnbanned", new
                    {
                        userId = bannedUserId,
                        username = user?.Nickname ?? string.Empty
                    });

                await _moderationLogService.LogAsync(
                    streamerId,
                    userId,
                    ChatModerationActions.Unban,
                    bannedUserId,
                    user?.Nickname);

                var bans = await _streamChatBanService.GetBannedUsersAsync(streamerId);
                return Ok(new { bans });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unbanning user {BannedUserId} from streamer {StreamerId}", bannedUserId, streamerId);
                return StatusCode(500, new { message = "Не удалось разбанить пользователя" });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
