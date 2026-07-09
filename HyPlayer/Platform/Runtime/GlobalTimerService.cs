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
using System.Timers;

namespace HyPlayer.Platform.Runtime;

public sealed class GlobalTimerService : IGlobalTimerService
{
    private readonly Timer _timer = new(1000)
    {
        AutoReset = true,
        Enabled = true,
    };

    public GlobalTimerService()
    {
        _timer.Elapsed += (_, _) => SecondTick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SecondTick;
}
