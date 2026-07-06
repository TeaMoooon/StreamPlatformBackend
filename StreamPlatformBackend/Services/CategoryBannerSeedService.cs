using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using System.Security.Cryptography;
using System.Text;

namespace StreamPlatformBackend.Services
{
    public interface ICategoryBannerSeedService
    {
        Task SeedAsync();
    }

    public class CategoryBannerSeedService : ICategoryBannerSeedService
    {
        private readonly AppDbContext _context;
        private readonly string _mediaPath;
        private readonly ILogger<CategoryBannerSeedService> _logger;

        public CategoryBannerSeedService(
            AppDbContext context,
            IConfiguration configuration,
            ILogger<CategoryBannerSeedService> logger)
        {
            _context = context;
            _mediaPath = configuration["Media:Path"] ?? "/var/www/streamplatform/media";
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            var categoriesDir = Path.Combine(_mediaPath, "categories");
            Directory.CreateDirectory(categoriesDir);

            var categories = await _context.StreamCategories.ToListAsync();
            var updated = false;

            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category.Slug))
                    continue;

                var slug = category.Slug.Trim().ToLowerInvariant();
                var fileName = $"{slug}.svg";
                var filePath = Path.Combine(categoriesDir, fileName);

                if (!File.Exists(filePath))
                {
                    var svg = BuildCategorySvg(category.Name ?? slug, slug);
                    await File.WriteAllTextAsync(filePath, svg);
                    _logger.LogInformation("Created category banner {FileName}", fileName);
                }

                var bannerUrl = $"/media/categories/{fileName}";
                if (category.BannerImageUrl != bannerUrl)
                {
                    category.BannerImageUrl = bannerUrl;
                    updated = true;
                }
            }

            if (updated)
                await _context.SaveChangesAsync();
        }

        private static string BuildCategorySvg(string title, string slug)
        {
            var (colorA, colorB) = GetGradientColors(slug);
            var safeTitle = EscapeXml(title);
            var shortTitle = safeTitle.Length > 18 ? safeTitle[..15] + "…" : safeTitle;

            return $"""
                <svg xmlns="http://www.w3.org/2000/svg" width="240" height="300" viewBox="0 0 240 300">
                  <defs>
                    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
                      <stop offset="0%" stop-color="{colorA}"/>
                      <stop offset="100%" stop-color="{colorB}"/>
                    </linearGradient>
                  </defs>
                  <rect width="240" height="300" rx="16" fill="url(#g)"/>
                  <rect x="12" y="12" width="216" height="276" rx="12" fill="rgba(0,0,0,0.18)"/>
                  <text x="120" y="155" text-anchor="middle" fill="#ffffff" font-family="Arial,sans-serif" font-size="22" font-weight="700">{shortTitle}</text>
                </svg>
                """;
        }

        private static (string a, string b) GetGradientColors(string slug)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(slug));
            var hue = hash[0] % 360;
            var a = $"hsl({hue}, 68%, 42%)";
            var b = $"hsl({(hue + 40) % 360}, 72%, 28%)";
            return (a, b);
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }

    public class CategoryBannerSeedHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CategoryBannerSeedHostedService> _logger;

        public CategoryBannerSeedHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<CategoryBannerSeedHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<ICategoryBannerSeedService>();
                await seeder.SeedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seed category banners");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
