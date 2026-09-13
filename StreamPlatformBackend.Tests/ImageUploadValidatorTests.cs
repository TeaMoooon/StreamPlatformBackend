using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StreamPlatformBackend.Services;

namespace StreamPlatformBackend.Tests
{
    public class ImageUploadValidatorTests
    {
        private static readonly byte[] JpegHeader =
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00
        };

        private static readonly byte[] PngHeader =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        };

        private static readonly byte[] WebpHeader =
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x2A, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P',
            (byte)'V', (byte)'P', (byte)'8', (byte)' '
        };

        private static ImageUploadValidator Create(long? maxBytes = null)
        {
            var values = new Dictionary<string, string?>();
            if (maxBytes.HasValue)
                values["Uploads:MaxImageBytes"] = maxBytes.Value.ToString();

            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new ImageUploadValidator(config);
        }

        private static IFormFile FormFile(string name, byte[] content)
        {
            var stream = new MemoryStream(content);
            var file = new MockFormFile(name, content.Length, stream);
            return file;
        }

        private sealed class MockFormFile : IFormFile
        {
            private readonly byte[] _content;
            public MockFormFile(string fileName, long length, Stream stream)
            {
                FileName = fileName;
                Length = length;
                _content = ((MemoryStream)stream).ToArray();
            }

            public string ContentType => "application/octet-stream";
            public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
            public IHeaderDictionary Headers { get; } = new HeaderDictionary();
            public long Length { get; }
            public string Name => "file";
            public string FileName { get; }

            public void CopyTo(Stream target) => target.Write(_content);
            public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
            {
                return target.WriteAsync(_content, cancellationToken).AsTask();
            }

            public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
        }

        [Theory]
        [InlineData("photo.jpg")]
        [InlineData("photo.jpeg")]
        public async Task ValidateAsync_AcceptsJpeg(string name)
        {
            var result = await Create().ValidateAsync(FormFile(name, JpegHeader));
            Assert.Equal(".jpg", result.Extension);
            Assert.Equal(JpegHeader, result.Content);
        }

        [Fact]
        public async Task ValidateAsync_AcceptsPng()
        {
            var result = await Create().ValidateAsync(FormFile("a.png", PngHeader));
            Assert.Equal(".png", result.Extension);
        }

        [Fact]
        public async Task ValidateAsync_AcceptsWebp()
        {
            var result = await Create().ValidateAsync(FormFile("a.webp", WebpHeader));
            Assert.Equal(".webp", result.Extension);
        }

        [Fact]
        public async Task ValidateAsync_RejectsExtensionContentMismatch()
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                Create().ValidateAsync(FormFile("fake.png", JpegHeader)));
            Assert.Contains("не соответствует", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ValidateAsync_RejectsFakeJpegExtensionWithPhpContent()
        {
            var php = System.Text.Encoding.UTF8.GetBytes("<?php system($_GET['c']); ?>.......");
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                Create().ValidateAsync(FormFile("shell.jpg", php)));
            Assert.Contains("не является допустимым изображением", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("x.exe")]
        [InlineData("x.gif")]
        [InlineData("x.svg")]
        [InlineData("x.php")]
        public async Task ValidateAsync_RejectsDisallowedExtension(string name)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Create().ValidateAsync(FormFile(name, JpegHeader)));
        }

        [Fact]
        public async Task ValidateAsync_RejectsOversizedDeclaredLength()
        {
            const int limit = 2048;
            var validator = Create(maxBytes: limit);
            var big = new byte[limit + 64];
            JpegHeader.CopyTo(big, 0);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                validator.ValidateAsync(FormFile("big.jpg", big)));
            Assert.Contains("Размер", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ValidateAsync_RejectsEmptyFile()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                Create().ValidateAsync(FormFile("empty.jpg", Array.Empty<byte>())));
        }

        [Fact]
        public void DetectImageFormat_RecognizesMagicBytes()
        {
            Assert.Equal(ImageUploadValidator.ImageFormat.Jpeg, ImageUploadValidator.DetectImageFormat(JpegHeader));
            Assert.Equal(ImageUploadValidator.ImageFormat.Png, ImageUploadValidator.DetectImageFormat(PngHeader));
            Assert.Equal(ImageUploadValidator.ImageFormat.Webp, ImageUploadValidator.DetectImageFormat(WebpHeader));
            Assert.Null(ImageUploadValidator.DetectImageFormat("not-an-image!!!!"u8));
        }
    }
}
