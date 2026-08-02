using System;

namespace HyPlayer.Platform.Runtime;

public interface IGlobalTimerService
{
    event EventHandler? SecondTick;
}