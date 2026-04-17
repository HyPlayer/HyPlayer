using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.Services.Abstractions;
using HyPlayer.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;



namespace HyPlayer.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : Page
    {
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
            base.OnNavigatedFrom(e);

            ViewModel.GetDataAsync().SafeFireAndForget();
        }

        private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
            //播放全部歌曲
            _playlist.Clear();
            await _playlist.AppendPlayListAsync(playList.PlaylistId);
            _playlist.PlaySourceId = $"pl{playList.PlaylistId}";
            await _playlist.MoveNextAsync(true);
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
               new PlaylistPrivacyRequest()
               {
                   Id = playList.PlaylistId
               });
            if (result.IsError)
            {
                Common.AddToTeachingTipLists("公开歌单失败", result.Error?.Message ?? "未知错误");
            }
            else
            {
                Common.AddToTeachingTipLists("成功公开歌单");
                _ = Common.PageBase?.LoadSongList();
            }
        }

        private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender?.As<MenuFlyoutItem>())?.CommandParameter;
            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistDeleteApi,
            new PlaylistDeleteRequest()
            {
                Id = playList.PlaylistId
            });
            if (result.IsError)
            {
                Common.AddToTeachingTipLists("删除歌单失败", result.Error?.Message ?? "未知错误");
            }
            else
            {
                Common.AddToTeachingTipLists("成功删除");
                _ = Common.PageBase?.LoadSongList();
                Common.NavigateRefresh();
            }
        }

        private void Card_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var button = sender?.As<ListViewItem>();
            if (button == null) return;
            var playlist = button.Tag as NCPlayList;
            Common.NavigatePage(typeof(SongListDetail), playlist);
        }
    }
}
