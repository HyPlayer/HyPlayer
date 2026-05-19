using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;

namespace HyPlayer.Services;

public sealed class PlayBarAutoHideService : IPlayBarAutoHideService
{
    private readonly Setting _setting;
    private readonly NotificationDispatcher _dispatcher;

    public PlayBarAutoHideService(Setting setting, NotificationDispatcher dispatcher)
    {
        _setting = setting;
        _dispatcher = dispatcher;
    }

    public int SecondCounter { get; set; }
    public bool IsVisible { get; set; } = true;

    public void Tick()
    {
        if (++SecondCounter < _setting.AutoHidePlaybarTime) return;
        if (!IsVisible) return;

        _dispatcher.Publish(new PlaybarVisibilityChangedNotification(false));
        IsVisible = false;
    }

    public void Show()
    {
        SecondCounter = 0;
        if (IsVisible) return;

        _dispatcher.Publish(new PlaybarVisibilityChangedNotification(true));
        IsVisible = true;
    }
}
