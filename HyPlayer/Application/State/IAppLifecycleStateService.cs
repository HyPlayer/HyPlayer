#nullable enable
using System;

namespace HyPlayer.Application.State;

public interface IAppLifecycleStateService
{
    bool IsInBackground { get; set; }
    event EventHandler? EnteredForeground;
    void NotifyEnteredForeground();
}