using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StaffDTO;
using StreamPlatformBackend.Models;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.Staff;
using StreamPlatformBackend.Services.NotificationService;
using System.Text.Json;

namespace StreamPlatformBackend.Services
{
    public interface ISupportTicketService
    {
        Task<(SupportTicketDto? ticket, string? error)> CreateAsync(int userId, CreateSupportTicketDto dto);
        Task<List<SupportTicketDto>> ListForUserAsync(int userId);
        Task<(SupportTicketDto? ticket, string? error)> GetForUserAsync(int userId, long ticketId);
        Task<(SupportTicketDto? ticket, string? error)> AddUserMessageAsync(int userId, long ticketId, string message);
        Task<List<SupportTicketDto>> ListForStaffAsync(string? status = null, int take = 50);
        Task<(SupportTicketDto? ticket, string? error)> GetForStaffAsync(long ticketId);
        Task<(SupportTicketDto? ticket, string? error)> AddStaffMessageAsync(int staffUserId, long ticketId, string message);
        Task<(SupportTicketDto? ticket, string? error)> UpdateStaffAsync(int staffUserId, long ticketId, UpdateSupportTicketDto dto);
    }

    public class SupportTicketService : ISupportTicketService
    {
        private const int MaxOpenTickets = 5;
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

        private readonly AppDbContext _context;
        private readonly IPlatformReportService _reports;
        private readonly IStaffAuditService _audit;
        private readonly INotificationRepository _notificationRepository;
        private readonly INotificationSender _notificationSender;
        private readonly ILogger<SupportTicketService> _logger;

        public SupportTicketService(
            AppDbContext context,
            IPlatformReportService reports,
            IStaffAuditService audit,
            INotificationRepository notificationRepository,
            INotificationSender notificationSender,
            ILogger<SupportTicketService> logger)
        {
            _context = context;
            _reports = reports;
            _audit = audit;
            _notificationRepository = notificationRepository;
            _notificationSender = notificationSender;
            _logger = logger;
        }

        public async Task<(SupportTicketDto? ticket, string? error)> CreateAsync(int userId, CreateSupportTicketDto dto)
        {
            var category = (dto.Category ?? string.Empty).Trim().ToLowerInvariant();
            if (!TicketCategories.IsKnown(category))
                return (null, "Неизвестная категория");

            var subject = (dto.Subject ?? string.Empty).Trim();
            if (subject.Length < 3)
                return (null, "Тема слишком короткая");
            if (subject.Length > 200)
                return (null, "Тема слишком длинная");

            var message = (dto.Message ?? string.Empty).Trim();
            if (message.Length < 5)
                return (null, "Сообщение слишком короткое");
            if (message.Length > 4000)
                return (null, "Сообщение слишком длинное");

            var openCount = await _context.SupportTickets.CountAsync(t =>
                t.UserId == userId
                && (t.Status == TicketStatuses.Open
                    || t.Status == TicketStatuses.InProgress
                    || t.Status == TicketStatuses.WaitingUser));
            if (openCount >= MaxOpenTickets)
                return (null, "Слишком много открытых тикетов. Дождитесь ответа или закройте старые");

            var dupSince = DateTime.UtcNow - DuplicateWindow;
            var duplicate = await _context.SupportTickets.AnyAsync(t =>
                t.UserId == userId
                && t.Category == category
                && t.Subject == subject
                && t.CreatedAt >= dupSince);
            if (duplicate)
                return (null, "Похожий тикет уже создан недавно");

            var ticket = new SupportTicket
            {
                UserId = userId,
                Category = category,
                Subject = subject,
                Status = TicketStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            ticket.Messages.Add(new SupportTicketMessage
            {
                AuthorUserId = userId,
                IsStaff = false,
                Body = message,
                CreatedAt = DateTime.UtcNow
            });

            _context.SupportTickets.Add(ticket);
            await _context.SaveChangesAsync();

            // Abuse/violations go through /api/reports ("Пожаловаться"), not auto-duplicated here.
            // Staff can still escalate a ticket to T&S manually.

            _logger.LogInformation("Support ticket {TicketId} created by user {UserId}", ticket.Id, userId);
            return (await MapAsync(ticket.Id), null);
        }

        public async Task<List<SupportTicketDto>> ListForUserAsync(int userId)
        {
            var ids = await _context.SupportTickets.AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.UpdatedAt)
                .Take(50)
                .Select(t => t.Id)
                .ToListAsync();

            var result = new List<SupportTicketDto>();
            foreach (var id in ids)
            {
                var mapped = await MapAsync(id);
                if (mapped != null) result.Add(mapped);
            }
            return result;
        }

        public async Task<(SupportTicketDto? ticket, string? error)> GetForUserAsync(int userId, long ticketId)
        {
            var ticket = await _context.SupportTickets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return (null, "Тикет не найден");
            if (ticket.UserId != userId) return (null, "Нет доступа");
            return (await MapAsync(ticketId), null);
        }

        public async Task<(SupportTicketDto? ticket, string? error)> AddUserMessageAsync(int userId, long ticketId, string message)
        {
            var body = (message ?? string.Empty).Trim();
            if (body.Length < 1) return (null, "Пустое сообщение");
            if (body.Length > 4000) return (null, "Сообщение слишком длинное");

            var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return (null, "Тикет не найден");
            if (ticket.UserId != userId) return (null, "Нет доступа");
            if (ticket.Status is TicketStatuses.Closed or TicketStatuses.Resolved)
                return (null, "Тикет закрыт");

            ticket.Messages.Add(new SupportTicketMessage
            {
                AuthorUserId = userId,
                IsStaff = false,
                Body = body,
                CreatedAt = DateTime.UtcNow
            });
            ticket.Status = TicketStatuses.Open;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return (await MapAsync(ticketId), null);
        }

        public async Task<List<SupportTicketDto>> ListForStaffAsync(string? status = null, int take = 50)
        {
            take = Math.Clamp(take, 1, 200);
            var query = _context.SupportTickets.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToLowerInvariant();
                if (TicketStatuses.IsKnown(normalized))
                    query = query.Where(t => t.Status == normalized);
            }

            var ids = await query
                .OrderByDescending(t => t.UpdatedAt)
                .Take(take)
                .Select(t => t.Id)
                .ToListAsync();

            var result = new List<SupportTicketDto>();
            foreach (var id in ids)
            {
                var mapped = await MapAsync(id);
                if (mapped != null) result.Add(mapped);
            }
            return result;
        }

        public async Task<(SupportTicketDto? ticket, string? error)> GetForStaffAsync(long ticketId)
        {
            var exists = await _context.SupportTickets.AnyAsync(t => t.Id == ticketId);
            if (!exists) return (null, "Тикет не найден");
            return (await MapAsync(ticketId), null);
        }

        public async Task<(SupportTicketDto? ticket, string? error)> AddStaffMessageAsync(
            int staffUserId,
            long ticketId,
            string message)
        {
            var body = (message ?? string.Empty).Trim();
            if (body.Length < 1) return (null, "Пустое сообщение");
            if (body.Length > 4000) return (null, "Сообщение слишком длинное");

            var ticket = await _context.SupportTickets.FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return (null, "Тикет не найден");

            ticket.Messages.Add(new SupportTicketMessage
            {
                AuthorUserId = staffUserId,
                IsStaff = true,
                Body = body,
                CreatedAt = DateTime.UtcNow
            });
            ticket.AssigneeUserId ??= staffUserId;
            if (ticket.Status is TicketStatuses.Open or TicketStatuses.WaitingUser)
                ticket.Status = TicketStatuses.InProgress;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _audit.WriteAsync(
                staffUserId,
                StaffAuditActions.TicketReplied,
                targetUserId: ticket.UserId,
                entityType: "SupportTicket",
                entityId: ticket.Id.ToString());

            await NotifyTicketOwnerAsync(
                ticket.UserId,
                ticket.Id,
                ticket.Subject,
                "Поддержка ответила на ваш тикет");

            return (await MapAsync(ticketId), null);
        }

        public async Task<(SupportTicketDto? ticket, string? error)> UpdateStaffAsync(
            int staffUserId,
            long ticketId,
            UpdateSupportTicketDto dto)
        {
            var ticket = await _context.SupportTickets
                .Include(t => t.Messages)
                .FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return (null, "Тикет не найден");

            if (dto.AssignToMe)
                ticket.AssigneeUserId = staffUserId;

            var notifyTerminalStatus = false;
            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var status = dto.Status.Trim().ToLowerInvariant();
                if (!TicketStatuses.IsKnown(status))
                    return (null, "Неизвестный статус");
                ticket.Status = status;
                if (status is TicketStatuses.Resolved or TicketStatuses.Closed)
                {
                    ticket.ResolvedAt = DateTime.UtcNow;
                    ticket.AssigneeUserId ??= staffUserId;
                    notifyTerminalStatus = true;
                }
            }

            if (dto.EscalateToReport)
            {
                var reason = string.IsNullOrWhiteSpace(dto.EscalateReason) ? "other" : dto.EscalateReason.Trim().ToLowerInvariant();
                var details = string.IsNullOrWhiteSpace(dto.EscalateDetails)
                    ? ticket.Messages.OrderBy(m => m.CreatedAt).FirstOrDefault()?.Body
                    : dto.EscalateDetails.Trim();
                var (ok, err) = await EscalateInternalAsync(ticket, staffUserId, reason, details);
                if (!ok) return (null, err);
            }

            ticket.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (notifyTerminalStatus)
            {
                await NotifyTicketOwnerAsync(
                    ticket.UserId,
                    ticket.Id,
                    ticket.Subject,
                    ticket.Status == TicketStatuses.Resolved
                        ? "Ваш тикет отмечен как решённый"
                        : "Ваш тикет закрыт");
            }

            return (await MapAsync(ticketId), null);
        }

        private async Task NotifyTicketOwnerAsync(int userId, long ticketId, string subject, string message)
        {
            var notification = new NotificationModel
            {
                UserId = userId,
                Type = NotificationType.SupportTicketReply,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    TicketId = ticketId,
                    Subject = subject,
                    Message = message
                }),
                CreatedAt = DateTime.UtcNow
            };

            await _notificationRepository.CreateNotificationAsync(notification);
            await _notificationSender.SendToUserAsync(notification);
        }

        private async Task<(bool ok, string? error)> EscalateInternalAsync(
            SupportTicket ticket,
            int actorUserId,
            string reason,
            string? details)
        {
            if (ticket.EscalatedReportId != null)
                return (true, null);

            if (!ReportReasons.IsKnown(reason))
                reason = ReportReasons.Other;

            var (report, error) = await _reports.CreateAsync(actorUserId, new CreatePlatformReportDto
            {
                TargetType = ReportTargetTypes.User,
                TargetUserId = ticket.UserId,
                Reason = reason,
                Details = $"Эскалация из тикета #{ticket.Id}: {details}".Trim()
            });

            // CreateAsync rejects self-report. Staff escalating about the ticket owner is fine;
            // but if user created abuse ticket about themselves this fails. For abuse tickets from users,
            // create report with a synthetic approach - use first staff as? Better: create report as system via direct insert.

            if (error != null && error.Contains("на себя"))
            {
                // User filed abuse about themselves / can't report self — create report targeting unknown with details only
                // Instead store report against no target? Required for user reports. Create with TargetUserId null for channel? 
                // For abuse ticket from user, escalate without targetUser self: treat as message-less user report with details only
                // Change: allow report without target for abuse escalation from ticket by inserting directly.
                var reportEntity = new PlatformReport
                {
                    ReporterUserId = actorUserId,
                    TargetType = ReportTargetTypes.User,
                    TargetUserId = null,
                    Reason = reason,
                    Details = $"Тикет #{ticket.Id} ({ticket.Subject}): {details}".Trim(),
                    Status = ReportStatuses.New,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.PlatformReports.Add(reportEntity);
                await _context.SaveChangesAsync();
                ticket.EscalatedReportId = reportEntity.Id;
                ticket.Category = TicketCategories.Abuse;
                return (true, null);
            }

            if (error != null)
                return (false, error);

            ticket.EscalatedReportId = report?.Id;
            ticket.Category = TicketCategories.Abuse;
            return (true, null);
        }

        private async Task<SupportTicketDto?> MapAsync(long id)
        {
            var ticket = await _context.SupportTickets.AsNoTracking()
                .Include(t => t.User)
                .Include(t => t.Assignee)
                .Include(t => t.Messages)
                    .ThenInclude(m => m.Author)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ticket == null) return null;

            return new SupportTicketDto
            {
                Id = ticket.Id,
                UserId = ticket.UserId,
                UserNickname = ticket.User?.Nickname,
                Category = ticket.Category,
                Subject = ticket.Subject,
                Status = ticket.Status,
                AssigneeUserId = ticket.AssigneeUserId,
                AssigneeNickname = ticket.Assignee?.Nickname,
                EscalatedReportId = ticket.EscalatedReportId,
                CreatedAt = ticket.CreatedAt,
                UpdatedAt = ticket.UpdatedAt,
                ResolvedAt = ticket.ResolvedAt,
                Messages = ticket.Messages
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new SupportTicketMessageDto
                    {
                        Id = m.Id,
                        AuthorUserId = m.AuthorUserId,
                        AuthorNickname = m.Author?.Nickname,
                        IsStaff = m.IsStaff,
                        Body = m.Body,
                        CreatedAt = m.CreatedAt
                    })
                    .ToList()
            };
        }
    }
}
