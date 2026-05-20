using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.AppState;

public sealed class AppLifecycleStateService : IAppLifecycleStateService
{
    public bool IsInBackground { get; set; }
}
