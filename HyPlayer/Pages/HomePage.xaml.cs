using AsyncAwaitBestPractices;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.ViewModels;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;



namespace HyPlayer.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : HomePageBase
    {
        public HomePage()
        {
            InitializeComponent();

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

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var playlist = button.CommandParameter as NCPlayList;
            Debug.WriteLine($"Card_Click: {playlist?.name}");
            Common.NavigatePage(typeof(SongListDetail), playlist, new Windows.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
        }

        private void SongCard_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var song = button.CommandParameter as NCSong;
            Debug.WriteLine($"Card_Click: {song?.songname}");
            HyPlayList.AppendNcSong(song);
            var targetPlayItemIndex =
                HyPlayList.List.FindIndex(t => t.PlayItem.Id == song.sid);
            HyPlayList.SongMoveTo(targetPlayItemIndex);

        }

        private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender as MenuFlyoutItem)?.CommandParameter;
            //播放全部歌曲
            HyPlayList.RemoveAllSong();
            await HyPlayList.AppendPlayList(playList.plid);
            HyPlayList.PlaySourceId = playList.plid;
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            var playList = (NCPlayList)(sender as MenuFlyoutItem)?.CommandParameter;
            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistPrivacyApi,
               new PlaylistPrivacyRequest()
               {
                   Id = playList.plid
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
            var playList = (NCPlayList)(sender as MenuFlyoutItem)?.CommandParameter;
            var result = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistDeleteApi,
            new PlaylistDeleteRequest()
            {
                Id = playList.plid
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
    }

    public class HomePageBase : AppPageBase<HomeViewModel>
    {
        public HomePageBase()
        {

        }
    }
}
