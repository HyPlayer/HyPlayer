using System;

namespace HyPlayer.Services.Abstractions;

public interface IGlobalTimerService
{
    event EventHandler? SecondTick;
}
