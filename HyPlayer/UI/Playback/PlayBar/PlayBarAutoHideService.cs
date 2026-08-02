using System;
using HyPlayer.Domain.Settings;

namespace HyPlayer.UI.Playback.PlayBar;

public sealed class PlayBarAutoHideService : IPlayBarAutoHideService
{
    private readonly UISettings _setting;

    public PlayBarAutoHideService(UISettings setting)
    {
        _setting = setting;
    }

    public event EventHandler<PlayBarVisibilityChangedEventArgs>? VisibilityChanged;

    public int SecondCounter { get; set; }
    public bool IsVisible { get; set; } = true;

    public void Tick()
    {
        if (++SecondCounter < _setting.AutoHidePlaybarTime) return;
        if (!IsVisible) return;

        VisibilityChanged?.Invoke(this, new PlayBarVisibilityChangedEventArgs(false));
        IsVisible = false;
    }

    public void Show()
    {
        SecondCounter = 0;
        if (IsVisible) return;

        VisibilityChanged?.Invoke(this, new PlayBarVisibilityChangedEventArgs(true));
        IsVisible = true;
    }
}