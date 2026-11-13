using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
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

    public HomePage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<HomeViewModel>();
    }

    private HomeViewModel ViewModel => (HomeViewModel)DataContext;

    private void RefreshRequested(RefreshContainer sender, RefreshRequestedEventArgs args)
    {
        var def = args.GetDeferral();
        Ioc.Default.GetRequiredService<HyPlayer.Platform.Runtime.Background.IBackgroundTaskRunner>()
            .Forget(async () =>
            {
                try { await ViewModel.GetDataAsync(true); }
                finally { def.Complete(); }
            }, "refresh home page");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        ViewModel.GetDataAsync().SafeFireAndForget();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Cancel();
    }

    private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var playList = sender?.As<MenuFlyoutItem>()?.CommandParameter as HomeContainerCardViewModel;
        if (playList is null) return;
        //播放全部歌曲
        await Ioc.Default.GetRequiredService<IAppNavigator>()
            .PlayAsync(new MusicResource.Playlist(playList.ActualId));
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
            _notification.ShowFailure("公开歌单失败", ex);
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
            _notification.ShowFailure("删除歌单失败", ex);
        }
    }

    private void CardList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeContainerCardViewModel playlist)
            _navigation.Navigate(typeof(SongListDetail), playlist.Container);
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var button = sender?.As<ListViewItem>();
        if (button == null) return;
        if (button.Tag is HomeContainerCardViewModel playlist)
            _navigation.Navigate(typeof(SongListDetail), playlist.Container);
    }
}