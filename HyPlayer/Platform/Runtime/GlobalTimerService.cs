using System;
using System.Timers;

namespace HyPlayer.Platform.Runtime;

public sealed class GlobalTimerService : IGlobalTimerService
{
    private readonly Timer _timer = new(1000)
    {
        AutoReset = true,
        Enabled = true
    };

    public GlobalTimerService()
    {
        _timer.Elapsed += (_, _) => SecondTick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SecondTick;
}