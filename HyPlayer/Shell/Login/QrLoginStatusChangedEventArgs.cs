using System;
using InfoBarSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace HyPlayer.Shell.Login;

public sealed class QrLoginStatusChangedEventArgs(Guid sessionId, string title, InfoBarSeverity severity) : EventArgs
{
    public Guid SessionId { get; } = sessionId;
    public string Title { get; } = title;
    public InfoBarSeverity Severity { get; } = severity;
}