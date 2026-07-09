using HyPlayer.Domain.Settings;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System;

namespace HyPlayer.UI.Playback.PlayBar;

public sealed class PlayBarAutoHideService : IPlayBarAutoHideService
{
    private readonly Setting _setting;

    public PlayBarAutoHideService(Setting setting)
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
