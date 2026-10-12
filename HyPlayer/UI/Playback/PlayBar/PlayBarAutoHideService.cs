using System;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;

namespace HyPlayer.UI.Playback.PlayBar;

public sealed class PlayBarAutoHideService : IPlayBarAutoHideService
{
    private readonly UISettings _setting;
    private readonly PlaybackSurfaceStore _surfaceStore;

    public PlayBarAutoHideService(UISettings setting, PlaybackSurfaceStore surfaceStore)
    {
        _setting = setting;
        _surfaceStore = surfaceStore;
    }

    public event EventHandler<PlayBarVisibilityChangedEventArgs>? VisibilityChanged;

    public int SecondCounter { get; set; }
    public bool IsVisible { get; set; } = true;

    public bool IsActive => _setting.AutoHidePlaybar ||
                            (_surfaceStore.IsExpanded &&
                             _setting.ExpandedControlStyle == ExpandedControlStyle.HyPlayer &&
                             _setting.ExpandedAutoCollapsePlaybar);

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