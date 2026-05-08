using HyPlayer.Controls;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Messages;

public sealed class MainPagePlaybarVisibilityChangedHandler(IUIStateService uiState)
    : INotificationHandler<PlaybarVisibilityChangedNotification>
{
    public void Handle(PlaybarVisibilityChangedNotification notification)
    {
        if (uiState.PageMain is MainPage mainPage)
            mainPage.OnPlaybarVisibilityChanged(notification.IsActivated);
    }
}

public sealed class CompactPlayerPlaybarVisibilityChangedHandler(IUIStateService uiState)
    : INotificationHandler<PlaybarVisibilityChangedNotification>
{
    public void Handle(PlaybarVisibilityChangedNotification notification)
    {
        if (uiState.PageCompactPlayer is CompactPlayerPage compactPlayerPage)
            compactPlayerPage.OnPlaybarVisibilityChanged(notification.IsActivated);
    }
}

public sealed class ExpandedPlayerUiNotificationHandler(IUIStateService uiState) :
    INotificationHandler<EnterForegroundFromBackgroundNotification>,
    INotificationHandler<PlaybarVisibilityChangedNotification>
{
    public void Handle(EnterForegroundFromBackgroundNotification notification)
    {
        if (uiState.PageExpandedPlayer is ExpandedPlayer expandedPlayer)
            expandedPlayer.OnEnteringForeground();
    }

    public void Handle(PlaybarVisibilityChangedNotification notification)
    {
        if (uiState.PageExpandedPlayer is ExpandedPlayer expandedPlayer)
            expandedPlayer.OnPlaybarVisibilityChanged(notification.IsActivated);
    }
}

public sealed class PlayBarEnterForegroundHandler(IUIStateService uiState)
    : INotificationHandler<EnterForegroundFromBackgroundNotification>
{
    public void Handle(EnterForegroundFromBackgroundNotification notification)
    {
        if (uiState.BarPlayBar is PlayBar playBar)
            playBar.OnEnteringForeground();
    }
}
