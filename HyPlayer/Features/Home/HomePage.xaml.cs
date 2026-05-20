using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playlist;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Notifications.Messages;
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
        private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
        private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();

        private HomeViewModel ViewModel => (HomeViewModel)DataContext;
        public HomePage()
        {
            InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<HomeViewModel>();
        }

        private void RefreshRequested(Microsoft.UI.Xaml.Controls.RefreshContainer sender, Microsoft.UI.Xaml.Controls.RefreshRequestedEventArgs args)
        {
            var def = args.GetDeferral();
            ViewModel.GetDataAsync().ContinueWith(t => def.Complete()).SafeFireAndForget();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ViewModel.GetDataAsync().SafeFireAndForget();
        }

        private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            //播放全部歌曲
            await Ioc.Default.GetRequiredService<IAppNavigator>()
                .PlayAsync(new MusicResource.Playlist(playList.PlaylistId));
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
               new PlaylistPrivacyRequest()
               {
                   Id = playList.PlaylistId
               });
            if (result.IsError)
            {
                _notification.ShowMessage("公开歌单失败", result.Error?.Message ?? "未知错误");
            }
            else
            {
                _notification.ShowMessage("成功公开歌单");
                WeakReferenceMessenger.Default.Send(new PlaylistCollectionChangedMessage());
            }
        }

        private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,
            new PlaylistDeleteRequest()
            {
                Id = playList.PlaylistId
            });
            if (result.IsError)
            {
                _notification.ShowMessage("删除歌单失败", result.Error?.Message ?? "未知错误");
            }
            else
            {
                _notification.ShowMessage("成功删除");
                WeakReferenceMessenger.Default.Send(new PlaylistCollectionChangedMessage());
                _navigation.NavigateRefresh();
            }
        }

        private void Card_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var button = sender?.As<ListViewItem>();
            if (button == null) return;
            var playlist = button.Tag as NCPlayList;
            _navigation.Navigate(typeof(SongListDetail), playlist);
        }
    }
}
