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
    }

    public class JwtService : IJwtService
    {
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;

        public JwtService(IConfiguration configuration)
        {
            _secretKey = configuration["Jwt:SecretKey"] ?? throw new ArgumentException("Jwt:SecretKey not set");
            _issuer = configuration["Jwt:Issuer"] ?? "StreamPlatformBackend";
            _audience = configuration["Jwt:Audience"] ?? "StreamPlatformUsers";
        }

        public string GenerateToken(UserModel user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Nickname),
                new Claim("Role", user.Role.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24), // Используем UTC
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
