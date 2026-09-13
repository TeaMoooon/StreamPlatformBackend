namespace StreamPlatformBackend.Services
{
    /// <summary>
    /// Ensures secrets live in appsettings.Local.json (gitignored) or env — not in committed appsettings.json.
    /// </summary>
    public static class LocalSecretsGuard
    {
        private static readonly string[] InsecurePlaceholders =
        {
            "CHANGE_ME",
            "your-secret-value",
            "your-super-secret-key-minimum-32-chars-long-here!",
            "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_32PLUS_CHARS",
            "CHANGE_ME_MATCH_NGINX_NOTIFY_SECRET",
            "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_32PLUS",
        };

        public static void EnsureConfigured(IConfiguration configuration)
        {
            var db = configuration.GetConnectionString("DefaultConnection");
            RequireSecret("ConnectionStrings:DefaultConnection", db, minLength: 20);

            var jwt = configuration["Jwt:SecretKey"];
            RequireSecret("Jwt:SecretKey", jwt, minLength: 32);

            var rtmp = configuration["Rtmp:Secret"];
            RequireSecret("Rtmp:Secret", rtmp, minLength: 16);
        }

        public static bool IsInsecurePlaceholder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var trimmed = value.Trim();
            foreach (var placeholder in InsecurePlaceholders)
            {
                if (trimmed.Equals(placeholder, StringComparison.Ordinal) ||
                    trimmed.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void RequireSecret(string key, string? value, int minLength)
        {
            if (string.IsNullOrWhiteSpace(value) || IsInsecurePlaceholder(value))
            {
                throw new InvalidOperationException(
                    $"{key} is missing or still a placeholder. " +
                    "Copy appsettings.Local.json.example → appsettings.Local.json (gitignored), " +
                    "set real secrets, chmod 600, and restart.");
            }

            if (value.Trim().Length < minLength)
            {
                throw new InvalidOperationException(
                    $"{key} must be at least {minLength} characters (set in appsettings.Local.json or env).");
            }
        }
    }
}
