using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
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
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace HyPlayer.UI.TeachingTips;

public sealed class TeachingTipService : ITeachingTipService
{
    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private int _secondCounter = 3;

    public TeachingTipService(IUIThreadDispatcher uiThreadDispatcher)
    {
        _uiThreadDispatcher = uiThreadDispatcher;
    }

    public Queue<KeyValuePair<string, string?>> Items { get; } = new();
    public object? Tip { get; set; }

    public void Clear() => Items.Clear();

    public void Roll(bool passiveRoll = true)
    {
        if (passiveRoll && _secondCounter-- > 0) return;
        _secondCounter = 3;

        if (Items.Count == 0)
        {
            _ = _uiThreadDispatcher.TryRunAsync(() =>
            {
                if (Tip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            if (Items.Count == 0) return;
            var (title, subtitle) = Items.Dequeue();
            if (Tip is not TeachingTip tip) return;
            tip.Title = title;
            tip.Subtitle = subtitle ?? "";
            if (!tip.IsOpen)
            {
                tip.IsOpen = true;
            }
            else
            {
                tip.IsOpen = false;
                tip.IsOpen = true;
            }
        });
    }

}
