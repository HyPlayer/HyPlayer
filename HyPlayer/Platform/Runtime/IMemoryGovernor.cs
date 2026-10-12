using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace HyPlayer.Platform.Runtime;

public interface IMemoryGovernor
{
    ulong WorkingSetBytes { get; }

    void Attach(Window window);

    void OnEnteredBackground();

    void OnLeavingBackground();

    Task<ulong> ReclaimAsync();
}
