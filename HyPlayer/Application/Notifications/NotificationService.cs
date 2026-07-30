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
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;

namespace HyPlayer.Application.Notifications;

/// <summary>
/// 通知服务实现，管理 UI 消息提示与线程调度
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IAppLifecycleStateService _lifecycle;
    private readonly ITeachingTipService _teachingTip;

    public NotificationService(IAppLifecycleStateService lifecycle, ITeachingTipService teachingTip)
    {
        _lifecycle = lifecycle;
        _teachingTip = teachingTip;
    }

    /// <inheritdoc />
    public void ShowMessage(string title, string? message = null)
    {
        _teachingTip.Items.Enqueue(new KeyValuePair<string, string?>(title, message));
        _ = InvokeOnUIThread(() =>
        {
            var tip = _teachingTip.Tip as TeachingTip;
            if (tip != null && !tip.IsOpen)
                _teachingTip.Roll(false);
        });
    }

    /// <inheritdoc />
    public IAsyncAction? InvokeOnUIThread(Action action)
    {
        if (_lifecycle.IsInBackground) return null;
        try
        {
            if (CoreApplication.Views.Count > 0)
                return CoreApplication.MainView.Dispatcher.RunAsync(
                    CoreDispatcherPriority.Normal,
                    () => { action(); });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification dispatch failed: {ex.Message}");
        }

        return null;
    }
}
