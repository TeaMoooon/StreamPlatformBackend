using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Hubs;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/streamers")]
    public class StreamChatSettingsController : ControllerBase
    {
        private readonly IRedisChatService _redisChatService;
        private readonly IStreamTeamService _streamTeamService;
        private readonly IStreamChatModerationLogService _moderationLogService;
        private readonly IHubContext<StreamHub> _hubContext;
        private readonly ILogger<StreamChatSettingsController> _logger;

        public StreamChatSettingsController(
            IRedisChatService redisChatService,
            IStreamTeamService streamTeamService,
            IStreamChatModerationLogService moderationLogService,
            IHubContext<StreamHub> hubContext,
            ILogger<StreamChatSettingsController> logger)
        {
            _redisChatService = redisChatService;
            _streamTeamService = streamTeamService;
            _moderationLogService = moderationLogService;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet("{streamerId:int}/chat/settings")]
        public async Task<IActionResult> GetSettings(int streamerId)
        {
            try
            {
                if (!await CanManageChatAsync(streamerId))
                    return Forbid();

                var settings = new StreamChatSettingsDto
                {
                    SlowModeSeconds = await _redisChatService.GetSlowModeSecondsAsync(streamerId),
                    ChatRules = await _redisChatService.GetChatRulesAsync(streamerId)
                };

                return Ok(settings);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat settings for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить настройки чата" });
            }
        }

        [HttpPut("{streamerId:int}/chat/settings")]
        public async Task<IActionResult> UpdateSettings(int streamerId, [FromBody] UpdateStreamChatSettingsDto dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!await CanManageChatAsync(streamerId))
                    return Forbid();

                var previousSlowMode = await _redisChatService.GetSlowModeSecondsAsync(streamerId);
                var slowModeChanged = false;
                var rulesChanged = false;

                if (dto.SlowModeSeconds.HasValue)
                {
                    var seconds = Math.Clamp(dto.SlowModeSeconds.Value, 0, ChatConstants.MaxSlowModeSeconds);
                    if (seconds != previousSlowMode)
                    {
                        await _redisChatService.SetSlowModeSecondsAsync(streamerId, seconds);
                        slowModeChanged = true;
                        await _moderationLogService.LogAsync(
                            streamerId,
                            userId,
                            ChatModerationActions.SlowMode,
                            details: seconds == 0 ? "Выключен" : $"{seconds} сек");
                    }
                }

                if (dto.ChatRules != null)
                {
                    var rules = dto.ChatRules.Trim();
                    if (rules.Length > ChatConstants.MaxChatRulesLength)
                        return BadRequest(new { message = $"Правила не длиннее {ChatConstants.MaxChatRulesLength} символов" });

                    var previousRules = await _redisChatService.GetChatRulesAsync(streamerId);
                    if (rules != previousRules)
                    {
                        await _redisChatService.SetChatRulesAsync(streamerId, rules);
                        rulesChanged = true;
                        await _moderationLogService.LogAsync(
                            streamerId,
                            userId,
                            ChatModerationActions.RulesUpdate,
                            details: string.IsNullOrEmpty(rules) ? "Очищены" : "Обновлены");
                    }
                }

                var slowModeSeconds = await _redisChatService.GetSlowModeSecondsAsync(streamerId);
                var chatRules = await _redisChatService.GetChatRulesAsync(streamerId);

                if (slowModeChanged || rulesChanged)
                {
                    await _hubContext.Clients.Group($"stream_{streamerId}")
                        .SendAsync("ChatSettingsChanged", new { slowModeSeconds, chatRules });
                }

                return Ok(new StreamChatSettingsDto
                {
                    SlowModeSeconds = slowModeSeconds,
                    ChatRules = chatRules
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating chat settings for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось обновить настройки чата" });
            }
        }

        [HttpGet("{streamerId:int}/chat/modlog")]
        public async Task<IActionResult> GetModerationLog(
            int streamerId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            try
            {
                if (!await CanManageChatAsync(streamerId))
                    return Forbid();

                var (items, total) = await _moderationLogService.GetLogsAsync(streamerId, page, pageSize);
                return Ok(new { items, total, page, pageSize });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Неверный токен авторизации" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting moderation log for streamer {StreamerId}", streamerId);
                return StatusCode(500, new { message = "Не удалось загрузить лог модерации" });
            }
        }

        private async Task<bool> CanManageChatAsync(int streamerId)
        {
            var userId = GetCurrentUserId();
            var access = await _streamTeamService.GetAccessAsync(streamerId, userId);
            return access != null && access.CanManageChat;
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
