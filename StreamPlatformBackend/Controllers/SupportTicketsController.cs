using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/support/tickets")]
    [Authorize]
    public class SupportTicketsController : ControllerBase
    {
        private readonly ISupportTicketService _tickets;

        public SupportTicketsController(ISupportTicketService tickets)
        {
            _tickets = tickets;
        }

        [HttpGet]
        public async Task<IActionResult> ListMine()
        {
            var items = await _tickets.ListForUserAsync(GetCurrentUserId());
            return Ok(items);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var (ticket, error) = await _tickets.GetForUserAsync(GetCurrentUserId(), id);
            if (error != null)
                return error == "Нет доступа" ? Forbid() : NotFound(new { message = error });
            return Ok(ticket);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSupportTicketDto dto)
        {
            var (ticket, error) = await _tickets.CreateAsync(GetCurrentUserId(), dto);
            if (error != null)
                return BadRequest(new { message = error });
            return Ok(ticket);
        }

        [HttpPost("{id:long}/messages")]
        public async Task<IActionResult> AddMessage(long id, [FromBody] AddSupportTicketMessageDto dto)
        {
            var (ticket, error) = await _tickets.AddUserMessageAsync(GetCurrentUserId(), id, dto.Message);
            if (error != null)
            {
                if (error == "Нет доступа") return Forbid();
                if (error == "Тикет не найден") return NotFound(new { message = error });
                return BadRequest(new { message = error });
            }
            return Ok(ticket);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var id))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return id;
        }
    }
}
