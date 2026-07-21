using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Services;
using System.Security.Claims;

namespace StreamPlatformBackend.Controllers
{
    [ApiController]
    [Route("api/staff/tickets")]
    [Authorize(Policy = "StaffOnly")]
    public class StaffTicketsController : ControllerBase
    {
        private readonly ISupportTicketService _tickets;
        private readonly ISettingsService _settings;
        private readonly IStaffAuditService _audit;

        public StaffTicketsController(
            ISupportTicketService tickets,
            ISettingsService settings,
            IStaffAuditService audit)
        {
            _tickets = tickets;
            _settings = settings;
            _audit = audit;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status = "open", [FromQuery] int take = 50)
        {
            var items = await _tickets.ListForStaffAsync(status, take);
            return Ok(items);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var (ticket, error) = await _tickets.GetForStaffAsync(id);
            if (error != null)
                return NotFound(new { message = error });
            return Ok(ticket);
        }

        [HttpPost("{id:long}/messages")]
        public async Task<IActionResult> AddMessage(long id, [FromBody] AddSupportTicketMessageDto dto)
        {
            var (ticket, error) = await _tickets.AddStaffMessageAsync(GetCurrentUserId(), id, dto.Message);
            if (error != null)
                return BadRequest(new { message = error });
            return Ok(ticket);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateSupportTicketDto dto)
        {
            var (ticket, error) = await _tickets.UpdateStaffAsync(GetCurrentUserId(), id, dto);
            if (error != null)
                return BadRequest(new { message = error });
            return Ok(ticket);
        }

        /// <summary>
        /// Reset stream key for the ticket owner (Support+). Does not reveal the new key to staff.
        /// </summary>
        [HttpPost("{id:long}/reset-stream-key")]
        public async Task<IActionResult> ResetStreamKey(long id)
        {
            var actorId = GetCurrentUserId();
            var (ticket, error) = await _tickets.GetForStaffAsync(id);
            if (error != null || ticket == null)
                return NotFound(new { message = error ?? "Тикет не найден" });

            try
            {
                await _settings.RegenerateStreamKeyAsync(ticket.UserId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            await _audit.WriteAsync(
                actorId,
                StaffAuditActions.StreamKeyReset,
                targetUserId: ticket.UserId,
                entityType: "SupportTicket",
                entityId: ticket.Id.ToString(),
                details: "Stream key regenerated via support ticket");

            await _tickets.AddStaffMessageAsync(
                actorId,
                id,
                "Ключ трансляции был сброшен. Новый ключ доступен владельцу канала в панели стрима.");

            var (updated, _) = await _tickets.GetForStaffAsync(id);
            return Ok(new { message = "Ключ сброшен", ticket = updated });
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                throw new UnauthorizedAccessException("Неверный токен авторизации");
            return userId;
        }
    }
}
