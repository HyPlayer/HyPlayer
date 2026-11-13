using System;
using HyPlayer.Application.Threading;
using HyPlayer.UI.TeachingTips;
using Microsoft.UI.Xaml.Controls;

namespace HyPlayer.Application.Notifications;

/// <summary>
///     通知服务实现，管理 UI 消息提示与线程调度
/// </summary>
public class NotificationService : INotificationService
{
    private readonly ITeachingTipService _teachingTip;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;

    public NotificationService(IUIThreadDispatcher uiThreadDispatcher, ITeachingTipService teachingTip)
    {
        _uiThreadDispatcher = uiThreadDispatcher;
        _teachingTip = teachingTip;
    }

    /// <inheritdoc />
    public void ShowMessage(string title, string? message = null)
    {
        Show(new TeachingTipItem(title, message));
    }

    /// <inheritdoc />
    public void ShowAction(string title, string? message, string actionText, Action action)
    {
        Show(new TeachingTipItem(title, message, actionText, action));
    }

    private void Show(TeachingTipItem item)
    {
        _teachingTip.Items.Enqueue(item);
        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            var tip = _teachingTip.Tip as TeachingTip;
            if (tip != null && !tip.IsOpen)
                _teachingTip.Roll(false);
        });
    }
}