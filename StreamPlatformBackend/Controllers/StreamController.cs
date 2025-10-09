using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamController : ControllerBase
{
    private readonly AppDbContext _db;

    public StreamController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetStreams()
    {
        var streams = await _db.Streams
            .Include(s => s.User)
            .Select(s => new StreamDto
            {
                Id = s.Id,
                StreamName = s.StreamName,
                IsLive = s.IsLive,
                HlsUrl = s.HlsUrl,
                UserNickname = s.User.Nickname
            })
            .ToListAsync();

        return Ok(streams);
    }

    [HttpPost]
    public async Task<IActionResult> CreateStream([FromBody] StreamModel stream)  // Явное указание Models.Stream
    {
        _db.Streams.Add(stream);
        await _db.SaveChangesAsync();
        return Ok(stream);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateStream(int id, [FromBody] StreamModel stream)  // Явное указание Models.Stream
    {
        var existingStream = await _db.Streams.FindAsync(id);
        if (existingStream == null) return NotFound();

        existingStream.IsLive = stream.IsLive;
        existingStream.HlsUrl = stream.HlsUrl;
        await _db.SaveChangesAsync();

        return Ok(existingStream);
    }
}