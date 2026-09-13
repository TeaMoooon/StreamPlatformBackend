using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Controllers;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Models.Stream;
using StreamPlatformBackend.Services;
using System.Text;
using System.Collections.Generic;
using Xunit;

namespace StreamPlatformBackend.Tests
{
    public class StreamCallbackControllerTests
    {
        private static int StatusCodeOf(IActionResult result)
        {
            return result switch
            {
                ObjectResult o => o.StatusCode ?? 0,
                StatusCodeResult s => s.StatusCode,
                _ => 0
            };
        }


        private static void SetRtmpSecretHeader(DefaultHttpContext httpContext, string? secret)
        {
            if (secret is null)
                return;
            httpContext.Request.Headers[StreamPlatformBackend.Services.RtmpCallbackAuth.HeaderName] = secret;
        }


        [Fact]
        public async Task OnStreamEnd_ShouldReturnUnauthorizedWhenSecretMismatch()
        {
            // Arrange
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("{\"stream\":\"live_1_abc\"}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "wrongSecret");

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: secret mismatch must be rejected)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnPing_ShouldReturnStatus500WhenUpdateHeartbeatThrows()
        {
            // Arrange
            const int userId = 1;
            const string streamKey = "live_1_abc";

            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            streamService
                .Setup(s => s.UpdateHeartbeatAsync(userId, streamKey))
                .ThrowsAsync(new Exception("redis down"));

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "irrelevantForPing"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "irrelevantForPing");

            // Act
            var result = await controller.OnPing(streamKey);

            // Assert (adversarial expectation: heartbeat exceptions must not be swallowed)
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        }

        [Fact]
        public async Task OnStreamStart_ShouldReturn400WhenMalformedJsonIsSent()
        {
            // Arrange
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("{\"stream\":")); // broken JSON

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: invalid JSON should be a 400, not a 500)
            var statusCode = result switch
            {
                ObjectResult o => o.StatusCode ?? 0,
                StatusCodeResult s => s.StatusCode,
                _ => 0
            };

            Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        }

        [Fact]
        public async Task OnStreamEnd_ShouldReturnUnauthorizedWhenSecretMissingAndPayloadInvalid()
        {
            // Arrange: /end currently returns 200 when stream payload can't be parsed,
            // even if secret is missing (bypass).
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/xml"; // wrong content-type
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("<not-json/>"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: invalid payload must not bypass secret check)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamStart_ShouldRejectWhenSecretOnlyInQueryString()
        {
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.QueryString = new QueryString("?secret=expectedSecret");
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("{\"stream\":\"live_1_abc\"}"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var result = await controller.OnStreamStart();

            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
            streamService.Verify(
                s => s.StartStreamAsync(It.IsAny<int>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task OnStreamStart_ShouldAcceptLoopbackQuerySecretAsMigrationFallback()
        {
            const int userId = 1;
            const string streamKey = "live_1_abc";

            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            streamService
                .Setup(s => s.StartStreamAsync(userId, streamKey))
                .ReturnsAsync(new StreamModel
                {
                    Id = 1,
                    UserId = userId,
                    StreamName = "test",
                    PublicId = "playback_1"
                });

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            httpContext.Request.QueryString = new QueryString("?secret=expectedSecret");
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes($"{{\"stream\":\"{streamKey}\"}}"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var result = await controller.OnStreamStart();

            Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
            streamService.Verify(s => s.StartStreamAsync(userId, streamKey), Times.Once);
        }

        [Fact]
        public async Task OnStreamStart_ShouldRejectNonLoopbackQuerySecret()
        {
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
            httpContext.Request.QueryString = new QueryString("?secret=expectedSecret");
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("{\"stream\":\"live_1_abc\"}"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var result = await controller.OnStreamStart();

            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
            streamService.Verify(
                s => s.StartStreamAsync(It.IsAny<int>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public async Task OnStreamEnd_ShouldReturnBadRequestWhenStreamKeyMissingPayloadInvalid()
        {
            // Arrange: invalid payload => streamKey == null/empty.
            // Current implementation returns Ok() even with correct secret (likely undesirable).
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "text/plain"; // wrong content-type
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("no stream field"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: invalid payload must be 400)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamStart_ShouldReturn400WhenStreamPropertyIsNotString()
        {
            // Arrange: JSON is valid, but stream is a number.
            // ReadStreamKeyFromCallbackAsync only catches JsonException (not InvalidOperationException),
            // so current code may return 500 instead of 400.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"stream\":123}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: wrong type => 400)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamEnd_ShouldReturn400WhenStreamPropertyIsNotString()
        {
            // Arrange: same issue on /end - should be handled as 400, not 500.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"stream\":123}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: wrong type => 400)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamStart_ShouldRejectStreamKeyNotBoundToUser()
        {
            // Arrange: /start trusts streamKey format and userId parsing,
            // but it doesn't verify that streamKey belongs to the given user.
            const int userId = 1;
            const string streamKeyInDb = "live_1_real";
            const string spoofedStreamKey = "live_1_spoofed";

            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            streamService
                .Setup(s => s.StartStreamAsync(userId, spoofedStreamKey))
                .ReturnsAsync(new StreamModel
                {
                    Id = 1,
                    UserId = userId,
                    StreamName = "test",
                    PublicId = "playback_1"
                });

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKeyInDb
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes($"{{\"stream\":\"{spoofedStreamKey}\"}}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: streamKey must be bound to user)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
            streamService.Verify(s => s.StartStreamAsync(userId, spoofedStreamKey), Times.Never);
        }

        [Fact]
        public async Task OnPing_ShouldReturnUnauthorizedForValidStreamKeyWithoutAnyAuth()
        {
            // Arrange: ping doesn't take/require secret; any caller with valid streamKey can update heartbeat.
            const int userId = 1;
            const string streamKey = "live_1_abc";

            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            streamService
                .Setup(s => s.UpdateHeartbeatAsync(userId, streamKey))
                .Returns(Task.CompletedTask);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "irrelevantForPing"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            // Act
            var result = await controller.OnPing(streamKey);

            // Assert (adversarial expectation: ping should be protected)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
            streamService.Verify(s => s.UpdateHeartbeatAsync(userId, streamKey), Times.Never);
        }

        [Fact]
        public async Task OnStreamStart_ShouldRejectWhenRtmpSecretIsNotConfigured_DefaultSecretIsProvided()
        {
            // Arrange: if configuration doesn't contain Rtmp:Secret, controller currently falls back
            // to a hardcoded default ("your-secret-value"), which should be treated as insecure.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            const int userId = 1;
            const string streamKey = "live_1_abc";

            streamService
                .Setup(s => s.StartStreamAsync(userId, streamKey))
                .ReturnsAsync(new StreamModel
                {
                    Id = 1,
                    UserId = userId,
                    StreamName = "test",
                    PublicId = "playback_1"
                });

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            // Intentionally no ["Rtmp:Secret"] in config.
            var configuration = new ConfigurationBuilder().Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes($"{{\"stream\":\"{streamKey}\"}}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "your-secret-value");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: default secret must not enable callbacks)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamEnd_ShouldRejectWhenRtmpSecretIsNotConfigured_DefaultSecretIsProvided()
        {
            // Arrange: same insecure default secret behavior, but for /end.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            const int userId = 1;
            const string streamKey = "live_1_abc";

            streamService
                .Setup(s => s.EndStreamAsync(userId, streamKey))
                .ReturnsAsync(true);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder().Build(); // no ["Rtmp:Secret"]

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes($"{{\"stream\":\"{streamKey}\"}}"));

            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "your-secret-value");

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: default secret must not enable callbacks)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnPing_ShouldRejectWhenRtmpSecretIsNotConfigured_DefaultSecretIsProvided()
        {
            // Arrange: same insecure default secret behavior, but for /ping.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);
            const int userId = 1;
            const string streamKey = "live_1_abc";

            streamService
                .Setup(s => s.UpdateHeartbeatAsync(userId, streamKey))
                .Returns(Task.CompletedTask);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);
            db.Users.Add(new UserModel
            {
                Id = userId,
                Email = "a@example.com",
                Nickname = "streamer",
                Role = "User",
                PasswordHash = "x",
                StreamKey = streamKey
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder().Build(); // no ["Rtmp:Secret"]

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "your-secret-value");

            // Act
            var result = await controller.OnPing(streamKey);

            // Assert (adversarial expectation: default secret must not enable callbacks)
            Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamStart_ShouldReturn400WhenFormPayloadHasInvalidUrlEncoding()
        {
            // Arrange: controller reads form field "name" when HasFormContentType=true.
            // A malformed form body should not turn into a 500.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("name=%ZZ"));
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: malformed form must yield 400, not 500)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamEnd_ShouldReturn400WhenMultipartBoundaryIsMalformed()
        {
            // Arrange: malformed multipart should not crash/throw while reading the form.
            // The current controller doesn't guard ReadFormAsync exceptions for form payloads.
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "multipart/form-data; boundary=";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not a multipart body"));
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamEnd();

            // Assert (adversarial expectation: malformed multipart must yield 400, not 500)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }

        [Fact]
        public async Task OnStreamStart_ShouldReturn400WhenStreamKeyHasInvalidFormat()
        {
            // Arrange: /start currently returns 401 for malformed stream key format.
            // For callbacks, this should be treated as a bad request (400).
            var streamService = new Mock<IStreamService>(MockBehavior.Strict);

            var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .Options;
            var db = new AppDbContext(dbOptions);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Rtmp:Secret"] = "expectedSecret"
                })
                .Build();

            var controller = new StreamCallbackController(
                streamService.Object,
                NullLogger<StreamCallbackController>.Instance,
                db,
                configuration);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("{\"stream\":\"live_X_abc\"}"));
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            };

            SetRtmpSecretHeader(httpContext, "expectedSecret");

            // Act
            var result = await controller.OnStreamStart();

            // Assert (adversarial expectation: invalid format => 400)
            Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        }
    }
}

