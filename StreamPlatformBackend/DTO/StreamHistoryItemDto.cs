namespace StreamPlatformBackend.DTO
{
    public class StreamHistoryItemDto
    {
        /// <summary>
        /// ID стрима.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Дата и время начала стрима.
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Дата и время окончания стрима.
        /// Может быть null, если стрим ещё не завершён.
        /// </summary>
        public DateTime? EndedAt { get; set; }

        /// <summary>
        /// Флаг наличия записи (архива стрима).
        /// </summary>
        public bool HasRecord { get; set; }

        /// <summary>
        /// Относительный путь к записи (если есть).
        /// Пример: media/users/5/streams/12/record.mp4
        /// </summary>
        public string? RecordPath { get; set; }
    }
}
