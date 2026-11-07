using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace HyPlayer.Platform.Runtime;

public interface IMemoryGovernor
{
    void Attach(Window window);

    void OnEnteredBackground();

    void OnLeavingBackground();

    void ScheduleNavigationReclaim();

    void OnExpandedPlayerVisibilityChanged(bool open);

    Task PauseAsync();

    void Resume();
}
