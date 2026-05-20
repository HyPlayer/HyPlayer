using HyPlayer.Services.Abstractions;
using System.Timers;

namespace HyPlayer.Services.Runtime;

public sealed class GlobalTimerService : IGlobalTimerService
{
    public Timer Timer { get; } = new(1000)
    {
        AutoReset = true,
        Enabled = true,
    };
}
