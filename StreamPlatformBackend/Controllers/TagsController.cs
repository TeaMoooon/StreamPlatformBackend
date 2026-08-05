using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;

namespace StreamPlatformBackend.Controllers
{
    /// <summary>
    /// Публичный поиск тегов (автодополнение в шапке и фильтр live).
    /// </summary>
    [ApiController]
    [Route("api/tags")]
    [AllowAnonymous]
    [Produces("application/json")]
    public class TagsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<TagsController> _logger;

        public TagsController(AppDbContext context, ILogger<TagsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Поиск тегов по имени/slug.
        /// </summary>
        /// <param name="search">Подстрока (минимум 1 символ). Пусто — популярные теги.</param>
        /// <param name="take">Максимум результатов (1–30, по умолчанию 12).</param>
        /// <response code="200">Список { id, name, slug }.</response>
        [HttpGet]
        [EnableRateLimiting("search")]
        public async Task<IActionResult> Search([FromQuery] string? search = null, [FromQuery] int take = 12)
        {
            try
            {
                take = Math.Clamp(take, 1, 30);
                var q = (search ?? string.Empty).Trim().ToLowerInvariant();

                var query = _context.Tags.AsNoTracking().AsQueryable();
                if (q.Length > 0)
                {
                    query = query.Where(t => t.Slug.Contains(q) || t.Name.ToLower().Contains(q));
                    query = query
                        .OrderByDescending(t => t.Slug == q)
                        .ThenByDescending(t => t.Slug.StartsWith(q))
                        .ThenBy(t => t.Name);
                }
                else
                {
                    query = query.OrderBy(t => t.Name);
                }

                var items = await query
                    .Take(take)
                    .Select(t => new { t.Id, t.Name, t.Slug })
                    .ToListAsync();

                return Ok(new { tags = items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка поиска тегов");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }
    }
}
