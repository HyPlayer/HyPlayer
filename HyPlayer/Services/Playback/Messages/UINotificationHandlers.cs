using HyPlayer.Controls;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.Messages;

public sealed class PlaybarVisibilityChangedHandler(IUIStateService uiState)
    : INotificationHandler<PlaybarVisibilityChangedNotification>
{
    public void Handle(PlaybarVisibilityChangedNotification notification)
    {
        if (uiState.PageMain is MainPage mainPage)
            mainPage.OnPlaybarVisibilityChanged(notification.IsActivated);
        if (uiState.PageExpandedPlayer is ExpandedPlayer expandedPlayer)
            expandedPlayer.OnPlaybarVisibilityChanged(notification.IsActivated);
        if (uiState.PageCompactPlayer is CompactPlayerPage compactPlayerPage)
            compactPlayerPage.OnPlaybarVisibilityChanged(notification.IsActivated);
    }
}

public sealed class EnterForegroundHandler(IUIStateService uiState)
    : INotificationHandler<EnterForegroundFromBackgroundNotification>
{
    public void Handle(EnterForegroundFromBackgroundNotification notification)
    {
        if (uiState.BarPlayBar is PlayBar playBar)
            playBar.OnEnteringForeground();
        if (uiState.PageExpandedPlayer is ExpandedPlayer expandedPlayer)
            expandedPlayer.OnEnteringForeground();
    }
}
