using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using System;
using System.Threading.Tasks;
using Frame = Windows.UI.Xaml.Controls.Frame;
using NavigationShellViewModel = HyPlayer.Shell.Navigation.NavigationShellViewModel;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;

namespace HyPlayer.Shell.Navigation.Services;

public interface IAppNavigator
{
    Task AppendAsync(MusicResource resource);

    void AttachNavigationView(NavigationView navigationView,
                              Frame rootFrame,
                              NavigationShellViewModel shellViewModel);

    void DetachNavigationView(NavigationView navigationView);

    Task NavigateAsync(AppRoute route);

    AppRoute? InferRoute(Type pageType, object? parameter);

    Task PlayAsync(MusicResource resource);

    Task PlaySongAsync(string songId);

    void SetPlaybackSource(MusicResource resource);

    void NavigateBack();

    void SyncNavigationViewSelection(Type pageType, object? parameter);

    void ToggleNavigationPane();
}
