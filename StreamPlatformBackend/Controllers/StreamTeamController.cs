using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/streamers")]
    public class StreamTeamController : ControllerBase
    {
        private readonly IStreamTeamService _streamTeamService;
        private readonly ILogger<StreamTeamController> _logger;

        public StreamTeamController(IStreamTeamService streamTeamService, ILogger<StreamTeamController> logger)
        {
            _streamTeamService = streamTeamService;
            _logger = logger;
        }

        [HttpGet("{streamerId:int}/team")]
        public async Task<IActionResult> GetTeam(int streamerId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var access = await _streamTeamService.GetAccessAsync(streamerId, userId);
                if (access == null || (access.Role != "Streamer" && !access.CanManageTeam))
                    return Forbid();

                var team = await _streamTeamService.GetTeamAsync(streamerId);
                return Ok(new { members = team, access });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting team for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить команду" });
            }
        }

        [HttpGet("by-nickname/{nickname}/team/access")]
        public async Task<IActionResult> GetAccessByNickname(string nickname)
        {
            try
            {
                var userId = GetCurrentUserId();
                var access = await _streamTeamService.GetAccessByNicknameAsync(nickname, userId);
                if (access == null)
                    return Ok(new StreamTeamAccessDto { StreamerNickname = nickname });

                return Ok(access);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting team access for {Nickname}", nickname);
                return StatusCode(500, new { message = "Не удалось проверить доступ" });
            }
        }

        [HttpPost("{streamerId:int}/team")]
        public async Task<IActionResult> AddMember(int streamerId, [FromBody] AddStreamTeamMemberDto dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var (success, error) = await _streamTeamService.AddMemberAsync(
                    streamerId,
                    userId,
                    dto.Nickname,
                    dto.Role);

                if (!success)
                    return BadRequest(new { message = error });

                var team = await _streamTeamService.GetTeamAsync(streamerId);
                return Ok(new { members = team });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding team member for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось добавить участника" });
            }
        }

        [HttpDelete("{streamerId:int}/team/{memberUserId:int}")]
        public async Task<IActionResult> RemoveMember(int streamerId, int memberUserId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var (success, error) = await _streamTeamService.RemoveMemberAsync(streamerId, userId, memberUserId);

                if (!success)
                    return BadRequest(new { message = error });

                var team = await _streamTeamService.GetTeamAsync(streamerId);
                return Ok(new { members = team });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing team member {MemberUserId} from streamer {StreamerId}", memberUserId, streamerId);
                return StatusCode(500, new { message = "Не удалось удалить участника" });
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
