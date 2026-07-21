namespace StreamPlatformBackend.Models.Enums
{
    public enum NotificationType
    {
        StreamStarted = 1,
        StreamEnded = 2,

        NewFollower = 3,
        //NewSubscriber
        //Donation

        System = 4,
        Warning = 5,
        Error = 6,
        SupportTicketReply = 7,
        PlatformSanction = 8,
        PlatformAppeal = 9
    }
}