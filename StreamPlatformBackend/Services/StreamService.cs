using Microsoft.EntityFrameworkCore;
using StreamPlatformBackend.Data;
using StreamPlatformBackend.Models.Stream;

namespace StreamPlatformBackend.Services
{
    public interface IStreamService
    {
        Task<StreamModel> StartStreamAsync(int userId, string streamKey);
        Task EndStreamAsync(int streamId);
    }

    public class StreamService : IStreamService
    {
        private readonly AppDbContext _context;

        public StreamService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<StreamModel> StartStreamAsync(int userId, string streamKey)
        {
            // Проверяем валидность streamKey и получаем пользователя
            var user = await _context.Users.Include(u => u.CurrentStream)
                .FirstOrDefaultAsync(u => u.Id == userId && u.StreamKey == streamKey);

            if (user == null)
                throw new UnauthorizedAccessException("Invalid stream key");

            // Если уже есть активный стрим, возвращаем его
            if (user.CurrentStream != null)
                return user.CurrentStream;

            // Создаем новый стрим с данными из предыдущего
            var newStream = new StreamModel
            {
                UserId = userId,
                StreamName = user.LastStreamName ?? $"Стрим {user.Nickname}",
                CategoryId = user.LastCategoryId ?? 1, // категория по умолчанию
                Tags = user.LastTags ?? Array.Empty<string>(),
                PreviewlUrl = user.LastPreviewlUrl,
                StartedAt = DateTime.UtcNow
            };

            _context.Streams.Add(newStream);
            user.CurrentStream = newStream;
            user.IsOnline = true;

            await _context.SaveChangesAsync();

            return newStream;
        }

        public async Task EndStreamAsync(int streamId)
        {
            var stream = await _context.Streams
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == streamId);

            if (stream != null)
            {
                stream.EndedAt = DateTime.UtcNow;

                // Сохраняем данные стрима в пользователе для следующего раза
                stream.User.LastStreamName = stream.StreamName;
                stream.User.LastPreviewlUrl = stream.PreviewlUrl;
                stream.User.LastCategoryId = stream.CategoryId;
                stream.User.LastTags = stream.Tags;
                stream.User.CurrentStream = null;
                stream.User.IsOnline = false;

                await _context.SaveChangesAsync();
            }
        }
    }
}
