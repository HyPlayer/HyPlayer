using System;
using Microsoft.UI.Xaml.Controls;

namespace HyPlayer.Pages;

public sealed record QrLoginStatusMessage(Guid SessionId, string Title, InfoBarSeverity Severity = InfoBarSeverity.Informational);
