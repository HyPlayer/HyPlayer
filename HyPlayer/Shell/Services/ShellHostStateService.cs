#nullable enable
using CommunityToolkit.WinUI.Controls;

namespace HyPlayer.Shell.Services;

public sealed class ShellHostStateService : IShellHostStateService
{
    public TitleBar? AppTitleBar { get; set; }

    public void ClearReference(TitleBar owner)
    {
        if (ReferenceEquals(AppTitleBar, owner)) AppTitleBar = null;
    }
}