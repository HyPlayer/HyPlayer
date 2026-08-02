namespace HyPlayer.Application.Notifications;

/// <summary>
///     通知服务，管理 UI 消息提示与线程调度
/// </summary>
public interface INotificationService
{
    /// <summary>显示 TeachingTip 消息</summary>
    void ShowMessage(string title, string? message = null);
}