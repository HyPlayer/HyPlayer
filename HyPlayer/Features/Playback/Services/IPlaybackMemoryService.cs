using System.Threading.Tasks;

namespace HyPlayer.Features.Playback.Services;

public interface IPlaybackMemoryService
{
    Task InitializeAsync();
    Task RestoreAsync();
    Task SaveNowAsync();
    Task ClearAsync();
}
