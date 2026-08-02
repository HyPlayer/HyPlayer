using System;

namespace HyPlayer.UI.Playback.PlayBar;

public interface IPlayBarAutoHideService
{
    int SecondCounter { get; set; }
    bool IsVisible { get; set; }
    event EventHandler<PlayBarVisibilityChangedEventArgs>? VisibilityChanged;
    void Tick();
    void Show();
}

public sealed class PlayBarVisibilityChangedEventArgs(bool isActivated) : EventArgs
{
    public bool IsActivated { get; } = isActivated;
}