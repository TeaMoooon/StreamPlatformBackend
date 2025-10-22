using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Controllers;

[ApiController]
[Route("api/stream")]
public class StreamController : ControllerBase
{
    private readonly IStreamService _streamService;

    public StreamController(IStreamService streamService)
    {
        _streamService = streamService;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartStream([FromBody] StartStreamRequest request)
    {
        try
        {
            var stream = await _streamService.StartStreamAsync(request.UserId, request.StreamKey);
            return Ok(new
            {
                success = true,
                streamId = stream.Id,
                streamName = stream.StreamName
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("end/{streamId}")]
    public async Task<IActionResult> EndStream(int streamId)
    {
        await _streamService.EndStreamAsync(streamId);
        return Ok(new { success = true });
    }
}

public class StartStreamRequest
{
    public int UserId { get; set; }
    public string StreamKey { get; set; } = string.Empty;
}