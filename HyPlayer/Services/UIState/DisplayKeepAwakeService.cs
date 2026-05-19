using HyPlayer.Services.Abstractions;
using Windows.System.Display;

namespace HyPlayer.Services;

public sealed class DisplayKeepAwakeService : IDisplayKeepAwakeService
{
    private readonly DisplayRequest _displayRequest = new();

    public void RequestActive() => _displayRequest.RequestActive();

    public void RequestRelease() => _displayRequest.RequestRelease();
}
