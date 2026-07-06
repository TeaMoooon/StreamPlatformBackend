using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Constants;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.StreamDTO;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamDashboardService
    {
        Task<StreamDashboardSettingsDto?> GetSettingsAsync(int streamerId);
        Task<(bool success, string? error)> UpdateSettingsAsync(
            int streamerId,
            int actorUserId,
            UpdateStreamDashboardSettingsDto dto);
    }

    public class StreamDashboardService : IStreamDashboardService
    {
        private readonly AppDbContext _context;
        private readonly IStreamTeamService _streamTeamService;
        private readonly ISettingsService _settingsService;

        public StreamDashboardService(
            AppDbContext context,
            IStreamTeamService streamTeamService,
            ISettingsService settingsService)
        {
            _context = context;
            _streamTeamService = streamTeamService;
            _settingsService = settingsService;
        }

        public async Task<StreamDashboardSettingsDto?> GetSettingsAsync(int streamerId)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Category)
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Tags)
                    .ThenInclude(st => st.Tag)
                .FirstOrDefaultAsync(u => u.Id == streamerId);

            if (user == null)
                return null;

            var subscriberCount = await _context.Subscriptions
                .CountAsync(s => s.TargetUserId == streamerId);

            var liveStream = user.CurrentStream is { EndedAt: null } stream ? stream : null;

            if (liveStream != null)
            {
                return new StreamDashboardSettingsDto
                {
                    StreamName = liveStream.StreamName,
                    CategoryId = liveStream.CategoryId,
                    CategoryName = liveStream.Category?.Name,
                    Tags = liveStream.Tags.Select(st => st.Tag.Slug).ToList(),
                    Language = user.StreamLanguage,
                    Announcement = user.StreamAnnouncement,
                    IsLive = true,
                    SubscriberCount = subscriberCount,
                    StartedAt = liveStream.StartedAt,
                };
            }

            return new StreamDashboardSettingsDto
            {
                StreamName = user.LastStreamName ?? string.Empty,
                CategoryId = user.LastCategoryId,
                CategoryName = user.LastCategoryId.HasValue
                    ? await _context.StreamCategories
                        .Where(c => c.Id == user.LastCategoryId.Value)
                        .Select(c => c.Name)
                        .FirstOrDefaultAsync()
                    : null,
                Tags = user.LastTags ?? new List<string>(),
                Language = user.StreamLanguage,
                Announcement = user.StreamAnnouncement,
                IsLive = false,
                SubscriberCount = subscriberCount,
                StartedAt = null,
            };
        }

        public async Task<(bool success, string? error)> UpdateSettingsAsync(
            int streamerId,
            int actorUserId,
            UpdateStreamDashboardSettingsDto dto)
        {
            var access = await _streamTeamService.GetAccessAsync(streamerId, actorUserId);
            if (access == null || !access.CanManageStream)
                return (false, "Недостаточно прав");

            var user = await _context.Users
                .Include(u => u.CurrentStream)!
                    .ThenInclude(s => s!.Tags)
                .FirstOrDefaultAsync(u => u.Id == streamerId);

            if (user == null)
                return (false, "Канал не найден");

            if (dto.Language != null)
            {
                var language = dto.Language.Trim().ToLowerInvariant();
                if (language.Length > 10)
                    return (false, "Код языка слишком длинный");
                user.StreamLanguage = string.IsNullOrEmpty(language) ? "ru" : language;
            }

            if (dto.Announcement != null)
            {
                var announcement = dto.Announcement.Trim();
                if (announcement.Length > ChatConstants.MaxStreamAnnouncementLength)
                    return (false, $"Анонс не длиннее {ChatConstants.MaxStreamAnnouncementLength} символов");
                user.StreamAnnouncement = announcement;
            }

            var liveStream = user.CurrentStream is { EndedAt: null } stream ? stream : null;

            if (liveStream != null)
            {
                var updateDto = new StreamUpdateDto
                {
                    StreamName = dto.StreamName,
                    CategoryId = dto.CategoryId,
                    Tags = dto.Tags?.ToArray()
                };

                var updated = await _settingsService.UpdateStreamSettingsAsync(streamerId, updateDto);
                if (!updated)
                    return (false, "Не удалось обновить текущий стрим");

                if (!string.IsNullOrWhiteSpace(dto.StreamName))
                    user.LastStreamName = dto.StreamName.Trim();

                if (dto.CategoryId.HasValue)
                    user.LastCategoryId = dto.CategoryId.Value;

                if (dto.Tags != null)
                    user.LastTags = dto.Tags
                        .Select(t => t.Trim().ToLowerInvariant())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct()
                        .ToList();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(dto.StreamName))
                    user.LastStreamName = dto.StreamName.Trim();

                if (dto.CategoryId.HasValue)
                    user.LastCategoryId = dto.CategoryId.Value;

                if (dto.Tags != null)
                    user.LastTags = dto.Tags
                        .Select(t => t.Trim().ToLowerInvariant())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Distinct()
                        .ToList();
            }

            await _context.SaveChangesAsync();
            return (true, null);
        }
    }
}
