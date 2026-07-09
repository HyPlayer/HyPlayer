using System;

namespace HyPlayer.UI.Playback.PlayBar;

public interface IPlayBarAutoHideService
{
    event EventHandler<PlayBarVisibilityChangedEventArgs>? VisibilityChanged;
    int SecondCounter { get; set; }
    bool IsVisible { get; set; }
    void Tick();
    void Show();
}

public sealed class PlayBarVisibilityChangedEventArgs(bool isActivated) : EventArgs
{
    public bool IsActivated { get; } = isActivated;
}
