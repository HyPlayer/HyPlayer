using System;

namespace HyPlayer.Application.State;

public sealed class AppLifecycleStateService : IAppLifecycleStateService
{
    public event EventHandler? EnteredForeground;

    public bool IsInBackground { get; set; }

    public void NotifyEnteredForeground()
    {
        EnteredForeground?.Invoke(this, EventArgs.Empty);
    }
}