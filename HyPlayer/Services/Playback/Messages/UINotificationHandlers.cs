using CommunityToolkit.Mvvm.Messaging;

namespace HyPlayer.Services.Playback.Messages;

public sealed class PlaybarVisibilityChangedHandler
    : INotificationHandler<PlaybarVisibilityChangedNotification>
{
    public void Handle(PlaybarVisibilityChangedNotification notification)
    {
        WeakReferenceMessenger.Default.Send(notification);
    }
}

public sealed class EnterForegroundHandler
    : INotificationHandler<EnterForegroundFromBackgroundNotification>
{
    public void Handle(EnterForegroundFromBackgroundNotification notification)
    {
        WeakReferenceMessenger.Default.Send(notification);
    }
}
