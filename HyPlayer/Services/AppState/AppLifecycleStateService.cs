using HyPlayer.Services.Abstractions;
using System;

namespace HyPlayer.Services.AppState;

public sealed class AppLifecycleStateService : IAppLifecycleStateService
{
    public event EventHandler? EnteredForeground;

    public bool IsInBackground { get; set; }

    public void NotifyEnteredForeground()
    {
        EnteredForeground?.Invoke(this, EventArgs.Empty);
    }
}
