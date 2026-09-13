using Microsoft.AspNetCore.Http;

namespace StreamPlatformBackend.Services
{
    public sealed class ValidatedImageUpload
    {
        public required string Extension { get; init; }
        public required byte[] Content { get; init; }
    }

    public interface IImageUploadValidator
    {
        /// <summary>
        /// Reads the upload (bounded by max size), validates extension + magic bytes,
        /// and returns canonical extension with raw content ready to write to disk.
        /// </summary>
        Task<ValidatedImageUpload> ValidateAsync(IFormFile file, CancellationToken ct = default);
    }

    public sealed class ImageUploadValidator : IImageUploadValidator
    {
        public const long DefaultMaxImageBytes = 5 * 1024 * 1024; // 5 MiB

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        private readonly long _maxBytes;

        public ImageUploadValidator(IConfiguration configuration)
        {
            _maxBytes = Math.Max(
                1024,
                configuration.GetValue("Uploads:MaxImageBytes", DefaultMaxImageBytes));
        }

        public long MaxBytes => _maxBytes;

        public async Task<ValidatedImageUpload> ValidateAsync(IFormFile file, CancellationToken ct = default)
        {
            if (file == null)
                throw new ArgumentException("Файл изображения не передан");

            if (file.Length <= 0)
                throw new ArgumentException("Файл изображения пуст");

            if (file.Length > _maxBytes)
                throw new ArgumentException($"Размер изображения не должен превышать {FormatMb(_maxBytes)}");

            var claimedExt = Path.GetExtension(file.FileName)?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(claimedExt) || !AllowedExtensions.Contains(claimedExt))
                throw new ArgumentException("Разрешены только форматы: JPG, PNG, WEBP");

            await using var input = file.OpenReadStream();
            await using var buffer = new MemoryStream(capacity: (int)Math.Min(file.Length, _maxBytes));

            var chunk = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), ct);
                if (read <= 0) break;

                total += read;
                if (total > _maxBytes)
                    throw new ArgumentException($"Размер изображения не должен превышать {FormatMb(_maxBytes)}");

                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }

            if (total <= 0)
                throw new ArgumentException("Файл изображения пуст");

            var content = buffer.ToArray();
            if (content.Length < 12)
                throw new ArgumentException("Файл слишком мал или повреждён");

            var detected = DetectImageFormat(content);
            if (detected == null)
                throw new ArgumentException("Содержимое файла не является допустимым изображением JPG/PNG/WEBP");

            if (!ExtensionMatchesFormat(claimedExt, detected.Value))
                throw new ArgumentException(
                    $"Расширение файла ({claimedExt}) не соответствует реальному формату ({detected})");

            return new ValidatedImageUpload
            {
                Extension = CanonicalExtension(detected.Value),
                Content = content
            };
        }

        public static ImageFormat? DetectImageFormat(ReadOnlySpan<byte> header)
        {
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return ImageFormat.Jpeg;

            if (header.Length >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return ImageFormat.Png;

            // RIFF....WEBP
            if (header.Length >= 12 &&
                header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
                return ImageFormat.Webp;

            return null;
        }

        private static bool ExtensionMatchesFormat(string extension, ImageFormat format) =>
            format switch
            {
                ImageFormat.Jpeg => extension is ".jpg" or ".jpeg",
                ImageFormat.Png => extension == ".png",
                ImageFormat.Webp => extension == ".webp",
                _ => false
            };

        private static string CanonicalExtension(ImageFormat format) =>
            format switch
            {
                ImageFormat.Jpeg => ".jpg",
                ImageFormat.Png => ".png",
                ImageFormat.Webp => ".webp",
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

        private static string FormatMb(long bytes) =>
            $"{Math.Ceiling(bytes / (1024d * 1024d))} МБ";

        public enum ImageFormat
        {
            Jpeg,
            Png,
            Webp
        }
    }
}
