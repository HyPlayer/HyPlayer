using System;
using System.Collections.Generic;
using System.Diagnostics;
using HyPlayer.Services.Abstractions;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;

namespace HyPlayer.Services;

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
