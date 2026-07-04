using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface IPlaybackMemoryService
{
    Task InitializeAsync();
    Task RestoreAsync();
    Task SaveNowAsync();
    Task ClearAsync();
}
