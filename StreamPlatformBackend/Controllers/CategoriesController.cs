using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Controllers
{
    /// <summary>Public category browse/search (header autocomplete).</summary>
    [ApiController]
    [Route("api/categories")]
    [AllowAnonymous]
    [EnableRateLimiting("search")]
    [Produces("application/json")]
    public class CategoriesController : ControllerBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<CategoriesController> _logger;

        public CategoriesController(ISettingsService settingsService, ILogger<CategoriesController> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 12;
                if (pageSize > 50) pageSize = 50;

                var (categories, total) = await _settingsService.GetCategoriesAsync(search, page, pageSize);
                return Ok(new
                {
                    page,
                    pageSize,
                    totalCategories = total,
                    categories
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка публичного списка категорий");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
            }
        }
    }
}
