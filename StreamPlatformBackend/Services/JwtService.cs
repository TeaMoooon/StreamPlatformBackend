using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using StreamPlatformBackend.Models.User;

namespace StreamPlatformBackend.Services
{
    public interface IJwtService
    {
        string GenerateToken(UserModel user);
        int AccessTokenSeconds { get; }
    }

    public class JwtService : IJwtService
    {
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _accessTokenMinutes;

        public JwtService(IConfiguration configuration)
        {
            _secretKey = configuration["Jwt:SecretKey"] ?? throw new ArgumentException("Jwt:SecretKey not set");
            _issuer = configuration["Jwt:Issuer"] ?? "StreamPlatformBackend";
            _audience = configuration["Jwt:Audience"] ?? "StreamPlatformUsers";
            _accessTokenMinutes = Math.Max(1, configuration.GetValue("Jwt:AccessTokenMinutes", 30));
        }

        public int AccessTokenSeconds => _accessTokenMinutes * 60;

        public string GenerateToken(UserModel user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Nickname),
                // Primary role claim used by IsInRole / [Authorize(Roles=...)]
                new Claim(ClaimTypes.Role, role),
                // Backward-compatible custom claim for older clients
                new Claim("Role", role)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
