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

    /// <summary>Получить информацию о стриме по никнейму</summary>
    [HttpGet("{username}")]
    public async Task<IActionResult> GetStreamByUsername(string username)
    {
        var user = await _userService.GetUserByNameAsync(username);
        if (user == null) return NotFound();

        var streamInfo = await _streamService.GetStreamInfoAsync(user.Id);
        if (streamInfo == null) return NotFound();

        // Асинхронное увеличение просмотров
        _ = Task.Run(async () => await _streamService.IncrementViewCountAsync(streamInfo.StreamId));

        return Ok(streamInfo);
    }
}
