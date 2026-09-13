using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.DTO.UserDTO;
using StreamPlatformBackend.Models.Enums;
using StreamPlatformBackend.Models.User;
using StreamPlatformBackend.Services;
using StreamPlatformBackend.Services.NotificationService;

namespace StreamPlatformBackend.Tests
{
    public class SettingsServiceTests
    {
        private static readonly byte[] JpegBytes =
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00
        };

        private static readonly byte[] PngBytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        };

        private static SettingsService CreateService(AppDbContext db, string mediaPath, long? maxBytes = null)
        {
            var values = new Dictionary<string, string?>
            {
                ["Media:Path"] = mediaPath
            };
            if (maxBytes.HasValue)
                values["Uploads:MaxImageBytes"] = maxBytes.Value.ToString();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            var catalogCache = new Mock<ICatalogCache>(MockBehavior.Loose);
            catalogCache
                .Setup(c => c.InvalidateChannelAsync(It.IsAny<int>(), It.IsAny<string?[]>()))
                .Returns(Task.CompletedTask);

            return new SettingsService(
                db,
                config,
                Mock.Of<IPasswordHasherService>(),
                Mock.Of<INotificationRepository>(),
                Mock.Of<INotificationSender>(),
                NullLogger<SettingsService>.Instance,
                catalogCache.Object,
                new ImageUploadValidator(config));
        }

        private static DbContextOptions<AppDbContext> CreateDbOptions()
        {
            return new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"db_{Guid.NewGuid():N}")
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        }

        private static IFormFile CreateFormFile(string fileName, byte[] content)
        {
            return new TestFormFile(fileName, content);
        }

        private sealed class TestFormFile : IFormFile
        {
            private readonly byte[] _content;
            public TestFormFile(string fileName, byte[] content)
            {
                FileName = fileName;
                _content = content;
                Length = content.Length;
            }

            public string ContentType => "application/octet-stream";
            public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
            public IHeaderDictionary Headers { get; } = new HeaderDictionary();
            public long Length { get; }
            public string Name => "file";
            public string FileName { get; }
            public void CopyTo(Stream target) => target.Write(_content);
            public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
                target.WriteAsync(_content, cancellationToken).AsTask();
            public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
        }

        [Theory]
        [InlineData("malware.exe")]
        [InlineData("payload.php")]
        [InlineData("script.svg")]
        [InlineData("archive.gif")]
        public async Task UpdateUserProfileAsync_ShouldRejectDisallowedImageExtensions(string fileName)
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaPath);

            try
            {
                await using var db = new AppDbContext(CreateDbOptions());
                db.Users.Add(new UserModel
                {
                    Id = 42,
                    Email = "uploader@example.com",
                    Nickname = "uploader",
                    Role = UserRole.User,
                    PasswordHash = "hash"
                });
                await db.SaveChangesAsync();

                var service = CreateService(db, mediaPath);
                var dto = new UserUpdateDataDto
                {
                    ProfileImage = CreateFormFile(fileName, JpegBytes)
                };

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UpdateUserProfileAsync(42, dto));

                Assert.Contains("JPG", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.False(Directory.EnumerateFiles(mediaPath, "*", SearchOption.AllDirectories).Any());
            }
            finally
            {
                if (Directory.Exists(mediaPath))
                    Directory.Delete(mediaPath, recursive: true);
            }
        }

        [Fact]
        public async Task UpdateUserProfileAsync_ShouldRejectMismatchedMagicBytes()
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaPath);

            try
            {
                await using var db = new AppDbContext(CreateDbOptions());
                db.Users.Add(new UserModel
                {
                    Id = 8,
                    Email = "bad@example.com",
                    Nickname = "baduser",
                    Role = UserRole.User,
                    PasswordHash = "hash"
                });
                await db.SaveChangesAsync();

                var service = CreateService(db, mediaPath);
                var dto = new UserUpdateDataDto
                {
                    // Claims PNG but content is JPEG
                    ProfileImage = CreateFormFile("avatar.png", JpegBytes)
                };

                await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UpdateUserProfileAsync(8, dto));
                Assert.False(Directory.EnumerateFiles(mediaPath, "*", SearchOption.AllDirectories).Any());
            }
            finally
            {
                if (Directory.Exists(mediaPath))
                    Directory.Delete(mediaPath, recursive: true);
            }
        }

        [Theory]
        [InlineData("avatar.jpg")]
        [InlineData("avatar.jpeg")]
        public async Task UpdateUserProfileAsync_ShouldAcceptJpeg(string fileName)
        {
            await AssertAcceptedUpload(fileName, JpegBytes, ".jpg");
        }

        [Fact]
        public async Task UpdateUserProfileAsync_ShouldAcceptPng()
        {
            await AssertAcceptedUpload("avatar.png", PngBytes, ".png");
        }

        private async Task AssertAcceptedUpload(string fileName, byte[] content, string expectedExt)
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaPath);

            try
            {
                await using var db = new AppDbContext(CreateDbOptions());
                db.Users.Add(new UserModel
                {
                    Id = 7,
                    Email = "ok@example.com",
                    Nickname = "okuser",
                    Role = UserRole.User,
                    PasswordHash = "hash"
                });
                await db.SaveChangesAsync();

                var service = CreateService(db, mediaPath);
                var dto = new UserUpdateDataDto
                {
                    ProfileImage = CreateFormFile(fileName, content)
                };

                await service.UpdateUserProfileAsync(7, dto);

                var user = await db.Users.SingleAsync(u => u.Id == 7);
                Assert.Equal($"/media/users/7/profile{expectedExt}", user.ProfileImage);
                Assert.True(File.Exists(Path.Combine(mediaPath, "users", "7", $"profile{expectedExt}")));
            }
            finally
            {
                if (Directory.Exists(mediaPath))
                    Directory.Delete(mediaPath, recursive: true);
            }
        }

        [Fact]
        public async Task UpdateUserProfileAsync_ShouldRejectOversizedImage()
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaPath);

            try
            {
                await using var db = new AppDbContext(CreateDbOptions());
                db.Users.Add(new UserModel
                {
                    Id = 9,
                    Email = "big@example.com",
                    Nickname = "biguser",
                    Role = UserRole.User,
                    PasswordHash = "hash"
                });
                await db.SaveChangesAsync();

                var huge = new byte[4096];
                JpegBytes.CopyTo(huge, 0);
                var service = CreateService(db, mediaPath, maxBytes: 2048);
                var dto = new UserUpdateDataDto
                {
                    ProfileImage = CreateFormFile("huge.jpg", huge)
                };

                var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                    service.UpdateUserProfileAsync(9, dto));
                Assert.Contains("Размер", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(mediaPath))
                    Directory.Delete(mediaPath, recursive: true);
            }
        }

        [Fact]
        public async Task RegenerateStreamKeyAsync_ShouldReturnLivePrefixedKeyBoundToUser()
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            await using var db = new AppDbContext(CreateDbOptions());
            db.Users.Add(new UserModel
            {
                Id = 99,
                Email = "key@example.com",
                Nickname = "keyuser",
                Role = UserRole.User,
                PasswordHash = "hash",
                StreamKey = "live_99_old"
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, mediaPath);
            var key = await service.RegenerateStreamKeyAsync(99);

            Assert.StartsWith("live_99_", key);
            Assert.NotEqual("live_99_old", key);
            Assert.Equal(key, (await db.Users.SingleAsync(u => u.Id == 99)).StreamKey);
        }

        [Fact]
        public async Task UploadStreamPreviewForUserAsync_ShouldRejectDisallowedExtension()
        {
            var mediaPath = Path.Combine(Path.GetTempPath(), $"media_{Guid.NewGuid():N}");
            await using var db = new AppDbContext(CreateDbOptions());
            db.Users.Add(new UserModel
            {
                Id = 5,
                Email = "preview@example.com",
                Nickname = "previewer",
                Role = UserRole.User,
                PasswordHash = "hash"
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, mediaPath);
            var file = CreateFormFile("preview.bmp", JpegBytes);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.UploadStreamPreviewForUserAsync(5, file));
        }
    }
}
