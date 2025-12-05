using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Services;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Models.Stream;


[ApiController]
[Route("api/streamers/{streamerId}/moderators")]
public class StreamModeratorsController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly RedisChatService _redisChatService;
    private readonly AppDbContext _context;

    public StreamModeratorsController(IUserService userService, RedisChatService redisChatService, AppDbContext context)
    {
        _userService = userService;
        _redisChatService = redisChatService;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetModerators(int streamerId)
    {
        var mods = await _context.StreamModerators
            .Where(m => m.StreamerId == streamerId)
            .Select(m => m.ModeratorId)
            .ToListAsync();
        return Ok(mods);
    }

    [HttpPost]
    public async Task<IActionResult> AddModerator(int streamerId, [FromBody] int moderatorId)
    {
        if (!_context.StreamModerators.Any(m => m.StreamerId == streamerId && m.ModeratorId == moderatorId))
        {
            _context.StreamModerators.Add(new StreamModerator { StreamerId = streamerId, ModeratorId = moderatorId });
            await _context.SaveChangesAsync();
        }

        // Обновляем Redis
        var mods = await _context.StreamModerators
            .Where(m => m.StreamerId == streamerId)
            .Select(m => m.ModeratorId)
            .ToListAsync();
        await _redisChatService.SetModeratorsAsync(streamerId, mods);

        return Ok();
    }

    [HttpDelete("{moderatorId}")]
    public async Task<IActionResult> RemoveModerator(int streamerId, int moderatorId)
    {
        var entity = await _context.StreamModerators.FirstOrDefaultAsync(m => m.StreamerId == streamerId && m.ModeratorId == moderatorId);
        if (entity != null)
        {
            _context.StreamModerators.Remove(entity);
            await _context.SaveChangesAsync();
        }

        // Обновляем Redis
        var mods = await _context.StreamModerators
            .Where(m => m.StreamerId == streamerId)
            .Select(m => m.ModeratorId)
            .ToListAsync();
        await _redisChatService.SetModeratorsAsync(streamerId, mods);

        return Ok();
    }
}
