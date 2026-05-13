using System;
using System.Collections.Generic;
using HyPlayer.Services.Abstractions;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;

namespace HyPlayer.Services;

/// <summary>
/// 通知服务实现，管理 UI 消息提示与线程调度
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IUIStateService _uiState;

    public NotificationService(IUIStateService uiState)
    {
        _uiState = uiState;
    }

    /// <inheritdoc />
    public void ShowMessage(string title, string? message = null)
    {
        _uiState.TeachingTipList.Enqueue(new KeyValuePair<string, string?>(title, message));
        _ = InvokeOnUIThread(() =>
        {
            var tip = _uiState.GlobalTip as Microsoft.UI.Xaml.Controls.TeachingTip;
            if (tip != null && !tip.IsOpen)
                _uiState.RollTeachingTip(false);
        });
    }

    /// <inheritdoc />
    public IAsyncAction? InvokeOnUIThread(Action action)
    {
        if (_uiState.IsInBackground) return null;
        try
        {
            if (CoreApplication.Views.Count > 0)
                return CoreApplication.MainView.Dispatcher.RunAsync(
                    CoreDispatcherPriority.Normal,
                    () => { action(); });
        }
        catch
        {
            // Ignore dispatcher errors
        }

        return null;
    }
}
