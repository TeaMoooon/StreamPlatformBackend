using System.ComponentModel.DataAnnotations;

namespace StreamPlatformBackend.DTO.UserDTO
{
    public class RefreshTokenRequestDto
    {
        /// <summary>
        /// Optional when the refresh token is supplied via HttpOnly cookie.
        /// </summary>
        public string? RefreshToken { get; set; }
    }
}
