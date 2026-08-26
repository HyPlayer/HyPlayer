using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Platform.Runtime.Background;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playlist;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Shell.Navigation.Services;
using WinRT;
using RefreshContainer = Microsoft.UI.Xaml.Controls.RefreshContainer;
using RefreshRequestedEventArgs = Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs;

namespace HyPlayer.Features.Home;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HomePage : Page
{
    private readonly IContainerManagementProvidable _containerManager =
        Ioc.Default.GetRequiredService<IContainerManagementProvidable>();

    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier =
        Ioc.Default.GetRequiredService<IPlaylistCollectionChangeNotifier>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public HomeViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<HomeViewModel>();


    public HomePage()
    {
        InitializeComponent();
    }

    private async void RefreshRequested(RefreshContainer sender, RefreshRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("刷新主页失败", ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _taskRunner.Forget(ViewModel.LoadAsync(), "load home page");
    }


    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var button = sender?.As<ListViewItem>();
        if (button == null) return;
        if (button.Tag is HomeContainerCardViewModel playlist)
            _navigation.Navigate(typeof(SongListDetail), playlist.Container);
    }
}