#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.Services.Abstractions;
using HyPlayer.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Me : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public Me()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<MeViewModel>();
    }
    private MeViewModel ViewModel => (MeViewModel)DataContext;
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter != null)
        {
            ViewModel.InitializeUserInfo((string)e.Parameter).SafeFireAndForget();
        }
        else
        {
            ViewModel.InitializeUserInfo(_auth.CurrentUser.Id).SafeFireAndForget();
        }
    }

    private void SonglistItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AppRoute target) return;
        _taskRunner.Forget(_navigator.NavigateAsync(target), "navigate to user songlist route");
    }
}
