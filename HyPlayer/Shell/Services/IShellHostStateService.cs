#nullable enable
using CommunityToolkit.WinUI.Controls;

namespace HyPlayer.Shell.Services;

public interface IShellHostStateService
{
    TitleBar? AppTitleBar { get; set; }
    void ClearReference(TitleBar owner);
}
