using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Services;

[ApiController]
[Route("api/[controller]")]
public class StreamViewController : ControllerBase
{
    private readonly IStreamService _streamService;
    private readonly IUserService _userService;
    private readonly ILogger<StreamViewController> _logger;

    public StreamViewController(IStreamService streamService, IUserService userService, ILogger<StreamViewController> logger)
    {
        _streamService = streamService;
        _userService = userService;
        _logger = logger;
    }

    // GET api/stream/dream - получить информацию о стриме по никнейму
    [HttpGet("{username}")]
    public async Task<IActionResult> GetStreamByUsername(string username)
    {
        try
        {
            _logger.LogInformation("Getting stream info for username: {Username}", username);

            var user = await _userService.GetUserByNameAsync(username);
            if (user == null)
            {
                _logger.LogWarning("User not found: {Username}", username);
                return NotFound(); // ← 404 без деталей
            }

            var streamInfo = await _streamService.GetStreamInfoAsync(user.Id);
            if (streamInfo == null)
            {
                _logger.LogWarning("User {Username} is not streaming", username);
                return NotFound(); // ← 404 без деталей
            }

            // Увеличиваем счетчик просмотров
            _ = Task.Run(async () =>
            {
                await _streamService.IncrementViewCountAsync(streamInfo.StreamId);
            });

            return Ok(streamInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stream info for username: {Username}", username);
            return StatusCode(500, "Internal server error");
        }
    }

    // GET api/stream - список активных стримов
    /*[HttpGet]
    public async Task<IActionResult> GetActiveStreams([FromQuery] int? categoryId = null)
    {
        try
        {
            var activeStreams = await _streamService.GetActiveStreamsAsync(categoryId);
            return Ok(activeStreams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active streams");
            return StatusCode(500, "Internal server error");
        }
    }*/
}