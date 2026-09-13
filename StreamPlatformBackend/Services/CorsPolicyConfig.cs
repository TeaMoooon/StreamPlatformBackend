using Microsoft.AspNetCore.Cors.Infrastructure;

namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Builds a strict credentialed CORS policy from <c>Cors:AllowedOrigins</c>.
    /// Never uses <c>AllowAnyOrigin</c> (incompatible with auth cookies).
    /// </summary>
    public static class CorsPolicyConfig
    {
        public const string PolicyName = "AllowFrontend";

        public static string[] ResolveAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
        {
            var configured = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                             ?? Array.Empty<string>();

            var origins = configured
                .Select(o => o?.Trim())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (origins.Length > 0)
                return origins;

            if (environment.IsDevelopment() || environment.IsEnvironment("Local"))
            {
                return new[]
                {
                    "https://192.168.1.167",
                    "http://192.168.1.167",
                    "http://localhost:3000",
                    "https://localhost:3000",
                    "http://127.0.0.1:3000"
                };
            }

            throw new InvalidOperationException(
                "Cors:AllowedOrigins must be configured with at least one explicit SPA origin. " +
                "AllowAnyOrigin is not permitted (auth cookies require AllowCredentials).");
        }

        public static void ApplyFrontendPolicy(CorsPolicyBuilder policy, IEnumerable<string> origins)
        {
            var list = origins?.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                       ?? Array.Empty<string>();

            if (list.Length == 0)
                throw new InvalidOperationException("CORS policy requires at least one allowed origin.");

            // Explicit origins only — never AllowAnyOrigin (breaks AllowCredentials / cookie auth).
            policy.WithOrigins(list)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }

        public static bool IsOriginAllowed(IEnumerable<string> allowedOrigins, string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return false;

            return allowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
        }
    }
}
