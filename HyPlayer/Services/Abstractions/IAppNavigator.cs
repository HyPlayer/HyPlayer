using System;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.ViewModels;
using Frame = Windows.UI.Xaml.Controls.Frame;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;

namespace HyPlayer.Services.Abstractions;

public interface IAppNavigator
{
    Task AppendAsync(MusicResource resource);

    void AttachNavigationView(NavigationView navigationView,
                              Frame rootFrame,
                              NavigationShellViewModel shellViewModel,
                              Func<Task>? loginRequiredAsync = null);

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
