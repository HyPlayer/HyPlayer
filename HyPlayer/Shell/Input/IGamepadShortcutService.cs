using Windows.UI.Core;

namespace HyPlayer.Shell.Input;

public interface IGamepadShortcutService
{
    void Attach(CoreWindow window);

    void Detach(CoreWindow window);
}
