using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playlist;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Services.Abstractions;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;

namespace HyPlayer.Features.Home
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private readonly IContainerManagementProvidable _containerManager = Ioc.Default.GetRequiredService<IContainerManagementProvidable>();
        private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
        private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
        private readonly IPlaylistCollectionChangeNotifier _playlistCollectionChangeNotifier = Ioc.Default.GetRequiredService<IPlaylistCollectionChangeNotifier>();

        private HomeViewModel ViewModel => (HomeViewModel)DataContext;
        public HomePage()
        {
            InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<HomeViewModel>();
        }

        private void RefreshRequested(Microsoft.UI.Xaml.Controls.RefreshContainer sender, Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs args)
        {
            var def = args.GetDeferral();
            ViewModel.GetDataAsync(forceRefresh: true).ContinueWith(t => def.Complete()).SafeFireAndForget();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ViewModel.GetDataAsync().SafeFireAndForget();
        }

        private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NeteasePlaylist)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            //播放全部歌曲
            await Ioc.Default.GetRequiredService<IAppNavigator>()
                .PlayAsync(new MusicResource.Playlist(playList.ActualId));
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NeteasePlaylist)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
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
            var playList = (NeteasePlaylist)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
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

        private void Card_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var button = sender?.As<ListViewItem>();
            if (button == null) return;
            var playlist = button.Tag as NeteasePlaylist;
            _navigation.Navigate(typeof(SongListDetail), playlist);
        }
    }
}
