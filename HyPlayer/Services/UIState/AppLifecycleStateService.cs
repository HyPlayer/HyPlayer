using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services;

public sealed class AppLifecycleStateService : IAppLifecycleStateService
{
    public bool IsInBackground { get; set; }
}
