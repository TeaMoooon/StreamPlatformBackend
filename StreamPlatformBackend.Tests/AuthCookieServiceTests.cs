using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class AuthCookieServiceTests
    {
        private static AuthCookieService CreateService(string? sameSite = "Lax", string? secure = "auto")
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["AuthCookies:SameSite"] = sameSite,
                    ["AuthCookies:Secure"] = secure
                })
                .Build();
            return new AuthCookieService(config);
        }

        private static (DefaultHttpContext Ctx, AuthTokenPair Pair) ArrangeHttps()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            var pair = new AuthTokenPair
            {
                UserId = 1,
                AccessToken = "access.jwt.token",
                RefreshToken = "opaque-refresh-token",
                ExpiresInSeconds = 1800
            };
            return (ctx, pair);
        }

        [Fact]
        public void AppendAuthCookies_ShouldSetHttpOnlyAccessAndRefresh_AndReadableSessionMarker()
        {
            var service = CreateService();
            var (ctx, pair) = ArrangeHttps();

            service.AppendAuthCookies(ctx.Response, ctx.Request, pair);

            var setCookies = ctx.Response.Headers.SetCookie.ToString();
            Assert.Contains($"{AuthCookieNames.Access}=", setCookies);
            Assert.Contains($"{AuthCookieNames.Refresh}=", setCookies);
            Assert.Contains($"{AuthCookieNames.Session}=1", setCookies);

            // Access + refresh must be HttpOnly; session marker must not.
            var cookies = setCookies.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            // Set-Cookie can be multiple header values
            var values = ctx.Response.Headers.SetCookie.ToArray();

            var access = Assert.Single(values, v => v.StartsWith($"{AuthCookieNames.Access}=", StringComparison.Ordinal));
            var refresh = Assert.Single(values, v => v.StartsWith($"{AuthCookieNames.Refresh}=", StringComparison.Ordinal));
            var session = Assert.Single(values, v => v.StartsWith($"{AuthCookieNames.Session}=", StringComparison.Ordinal));

            Assert.Contains("httponly", access, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", refresh, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("httponly", session, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("secure", access, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", access, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(pair.AccessToken, session, StringComparison.Ordinal);
            Assert.DoesNotContain(pair.RefreshToken, session, StringComparison.Ordinal);
        }

        [Fact]
        public void AppendAuthCookies_ShouldNeverPutRawTokensInSessionMarker()
        {
            var service = CreateService();
            var (ctx, pair) = ArrangeHttps();
            service.AppendAuthCookies(ctx.Response, ctx.Request, pair);

            var session = Assert.Single(
                ctx.Response.Headers.SetCookie.ToArray(),
                v => v.StartsWith($"{AuthCookieNames.Session}=", StringComparison.Ordinal));

            Assert.StartsWith($"{AuthCookieNames.Session}=1", session, StringComparison.Ordinal);
            Assert.DoesNotContain(pair.AccessToken, session);
            Assert.DoesNotContain(pair.RefreshToken, session);
        }

        [Fact]
        public void ClearAuthCookies_ShouldExpireAllAuthCookies()
        {
            var service = CreateService();
            var (ctx, _) = ArrangeHttps();

            service.ClearAuthCookies(ctx.Response, ctx.Request);

            var values = ctx.Response.Headers.SetCookie.ToArray();
            Assert.Contains(values, v => v.StartsWith($"{AuthCookieNames.Access}=", StringComparison.Ordinal));
            Assert.Contains(values, v => v.StartsWith($"{AuthCookieNames.Refresh}=", StringComparison.Ordinal));
            Assert.Contains(values, v => v.StartsWith($"{AuthCookieNames.Session}=", StringComparison.Ordinal));
        }

        [Fact]
        public void ReadTokens_ShouldReturnNullWhenMissing()
        {
            var service = CreateService();
            var ctx = new DefaultHttpContext();

            Assert.Null(service.ReadAccessToken(ctx.Request));
            Assert.Null(service.ReadRefreshToken(ctx.Request));
        }

        [Fact]
        public void ReadTokens_ShouldReturnCookieValues()
        {
            var service = CreateService();
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Cookie = $"{AuthCookieNames.Access}=jwt-here; {AuthCookieNames.Refresh}=refresh-here";

            Assert.Equal("jwt-here", service.ReadAccessToken(ctx.Request));
            Assert.Equal("refresh-here", service.ReadRefreshToken(ctx.Request));
        }
    }
}
