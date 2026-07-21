using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;

namespace StreamPlatformBackend.Services
{
    public interface IStaffStatsService
    {
        Task<StaffQueueStatsDto> GetQueueStatsAsync(TimeSpan staleAfter);
    }

    public class StaffStatsService : IStaffStatsService
    {
        private readonly AppDbContext _context;

        public StaffStatsService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<StaffQueueStatsDto> GetQueueStatsAsync(TimeSpan staleAfter)
        {
            var staleBefore = DateTime.UtcNow - staleAfter;
            var now = DateTime.UtcNow;

            return new StaffQueueStatsDto
            {
                ReportsNew = await _context.PlatformReports.CountAsync(r => r.Status == ReportStatuses.New),
                ReportsInProgress = await _context.PlatformReports.CountAsync(r => r.Status == ReportStatuses.InProgress),
                TicketsOpen = await _context.SupportTickets.CountAsync(t => t.Status == TicketStatuses.Open),
                TicketsInProgress = await _context.SupportTickets.CountAsync(t => t.Status == TicketStatuses.InProgress),
                TicketsWaitingUser = await _context.SupportTickets.CountAsync(t => t.Status == TicketStatuses.WaitingUser),
                AppealsOpen = await _context.PlatformAppeals.CountAsync(a =>
                    a.Status == AppealStatuses.Open || a.Status == AppealStatuses.InReview),
                ActiveSanctions = await _context.PlatformSanctions.CountAsync(s =>
                    s.Status == PlatformSanctionStatuses.Active &&
                    (s.ExpiresAt == null || s.ExpiresAt > now)),
                StaleReports = await _context.PlatformReports.CountAsync(r =>
                    (r.Status == ReportStatuses.New || r.Status == ReportStatuses.InProgress) &&
                    r.UpdatedAt < staleBefore),
                StaleTickets = await _context.SupportTickets.CountAsync(t =>
                    (t.Status == TicketStatuses.Open ||
                     t.Status == TicketStatuses.InProgress ||
                     t.Status == TicketStatuses.WaitingUser) &&
                    t.UpdatedAt < staleBefore)
            };
        }
    }
}
