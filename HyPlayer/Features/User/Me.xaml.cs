#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.Features.Account.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Shell.Navigation.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.User;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Me : Page
{
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public MeViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<MeViewModel>();

    public Me()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var userId = e.Parameter as string ?? _auth.CurrentUser?.ActualId;
        if (!string.IsNullOrWhiteSpace(userId))
            _taskRunner.Forget(ViewModel.LoadAsync(userId), "load user page");
    }

    private void SonglistItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRoute target) return;
        _taskRunner.Forget(_navigator.NavigateAsync(target), "navigate to user songlist route");
    }

    private void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MusicResource resource) return;
        _taskRunner.Forget(_navigator.PlayAsync(resource), "play user songlist");
    }
}
