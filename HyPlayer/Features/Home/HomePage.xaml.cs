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

    private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var playList = sender?.As<MenuFlyoutItem>()?.CommandParameter as HomeContainerCardViewModel;
        if (playList is null) return;
        //播放全部歌曲
        await _navigator.PlayAsync(new MusicResource.Playlist(playList.ActualId));
    }

    private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
    {
        var playList = sender?.As<MenuFlyoutItem>()?.CommandParameter as HomeContainerCardViewModel;
        if (playList is null) return;
        try
        {
            await _containerManager.SetContainerPrivacyAsync(playList.ActualId, true);
            _notification.ShowMessage("成功公开歌单");
            _playlistCollectionChangeNotifier.NotifyChanged();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("公开歌单失败", ex.Message);
        }
    }

    private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
    {
        var playList = sender?.As<MenuFlyoutItem>()?.CommandParameter as HomeContainerCardViewModel;
        if (playList is null) return;
        try
        {
            await _containerManager.DeleteContainerAsync(playList.ActualId);
            _notification.ShowMessage("成功删除");
            _playlistCollectionChangeNotifier.NotifyChanged();
            _navigation.NavigateRefresh();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("删除歌单失败", ex.Message);
        }
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var button = sender?.As<ListViewItem>();
        if (button == null) return;
        if (button.Tag is HomeContainerCardViewModel playlist)
            _navigation.Navigate(typeof(SongListDetail), playlist.Container);
    }
}