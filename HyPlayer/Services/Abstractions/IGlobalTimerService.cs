using System.Timers;

namespace HyPlayer.Services.Abstractions;

public interface IGlobalTimerService
{
    Timer Timer { get; }
}
