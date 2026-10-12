using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace HyPlayer.Platform.Runtime;

public interface IMemoryGovernor
{
    ulong WorkingSetBytes { get; }

    void Attach(Window window);

    void OnEnteredBackground();

    void OnLeavingBackground();

    void ScheduleNavigationReclaim();

    void OnExpandedPlayerVisibilityChanged(bool open);

    Task<ulong> ReclaimAsync();
}
