using System;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.Pages;
using System.Collections.Generic;
using NeteaseCloudMusicApi;
using System.Linq;
using Newtonsoft.Json.Linq;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class PlaylistItem : UserControl, IDisposable
    {
        private NCPlayList playList;

        public PlaylistItem(NCPlayList playList)
        {
            InitializeComponent();
            this.playList = playList;
            Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    ImageContainer.Source =
                        new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                    TextBlockPLName.Text = playList.name;
                    TextBlockPLAuthor.Text = playList.creater.name;
                });
            });
            StoryboardIn.Begin();
        }

        private void UIElement_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongListExpand", ImageContainer);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongListExpandAcrylic", GridInfo);
            Common.NavigatePage(typeof(SongListDetail), playList, new DrillInNavigationTransitionInfo());
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            StoryboardOut.Begin();
        }

        private void UIElement_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            StoryboardIn.Begin();
        }

        private void UIElement_OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            StoryboardIn.Begin();
        }

        private async void PlayAllBtn_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            //播放全部歌曲
            HyPlayList.List.Clear();
            HyPlayList.SongAppendDone();
            await HyPlayList.AppendPlayList(playList.plid);
            HyPlayList.SongAppendDone();
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }

        public void Dispose()
        {
            playList = null;
            ImageContainer.Source = null;
        }

        private async void ItemPublicPlayList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistPrivacy,
                    new Dictionary<string, object>()
                    {
                        { "id", playList.plid }
                    });
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("公开歌单失败", ex.Message);
                return;
            }

            Common.ShowTeachingTip("成功公开歌单");
            Common.PageBase.LoadSongList();
        }

        private async void ItemDelPlayList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDelete,
                    new Dictionary<string, object>()
                    {
                        { "ids", playList.plid }
                    });
                Common.ShowTeachingTip("成功删除");
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip("发生错误", ex.Message);
            }

            
            Common.PageBase.LoadSongList();
            Common.NavigateRefresh();
        }
    }
}