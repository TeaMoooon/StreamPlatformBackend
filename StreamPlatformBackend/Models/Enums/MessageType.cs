namespace StreamPlatformBackend.Models.Enums
{
    public enum MessageType
    {
        Regular,        // Обычное сообщение
        Donation,       // Сообщение с донатом
        Moderator,      // Сообщение модератора
        System,         // Системное сообщение
        Highlighted,    // Выделенное сообщение
        FirstMessage    // Первое сообщение в чате
    }
}
