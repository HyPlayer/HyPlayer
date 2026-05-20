#nullable enable
using System;

namespace HyPlayer.Services.Abstractions;

public interface IAppLifecycleStateService
{
    event EventHandler? EnteredForeground;
    bool IsInBackground { get; set; }
    void NotifyEnteredForeground();
}
