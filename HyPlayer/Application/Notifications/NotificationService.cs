using System.Collections.Generic;
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
        _teachingTip.Items.Enqueue(new KeyValuePair<string, string?>(title, message));
        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            var tip = _teachingTip.Tip as TeachingTip;
            if (tip != null && !tip.IsOpen)
                _teachingTip.Roll(false);
        });
    }
}