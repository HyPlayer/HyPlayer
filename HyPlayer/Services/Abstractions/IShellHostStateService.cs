#nullable enable
using CommunityToolkit.WinUI.Controls;

namespace HyPlayer.Services.Abstractions;

public interface IShellHostStateService
{
    TitleBar? AppTitleBar { get; set; }
    void ClearReference(TitleBar owner);
}
