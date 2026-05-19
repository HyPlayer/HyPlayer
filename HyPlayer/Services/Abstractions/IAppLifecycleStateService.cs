#nullable enable
namespace HyPlayer.Services.Abstractions;

public interface IAppLifecycleStateService
{
    bool IsInBackground { get; set; }
}
