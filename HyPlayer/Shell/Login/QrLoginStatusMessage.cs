using Microsoft.UI.Xaml.Controls;
using System;

namespace HyPlayer.Shell.Login;

public sealed record QrLoginStatusMessage(Guid SessionId, string Title, InfoBarSeverity Severity = InfoBarSeverity.Informational);
