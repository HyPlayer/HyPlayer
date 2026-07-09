#nullable enable
using System;

namespace HyPlayer.Application.State;

public interface IAppLifecycleStateService
{
    event EventHandler? EnteredForeground;
    bool IsInBackground { get; set; }
    void NotifyEnteredForeground();
}
