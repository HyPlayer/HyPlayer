using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls
{
    public sealed partial class SongsList : UserControl
    {
        public SongsList()
        {
            this.InitializeComponent();
        }
        public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
  "Songs", typeof(ObservableCollection<NCSong>)
  ,
  typeof(SongsList),
  new PropertyMetadata(null)
);
        public static readonly DependencyProperty PlayListProperty = DependencyProperty.Register(
"playList", typeof(NCPlayList)
,
typeof(SongsList),
new PropertyMetadata(null)
);


        public ObservableCollection<NCSong> Songs
        {
            get { return (ObservableCollection<NCSong>)GetValue(SongsProperty); }
            set { SetValue(SongsProperty, value); }
        }

        public bool IsManualSelect { get; private set; }
        public NCPlayList playList
        {
            get { return (NCPlayList)GetValue(PlayListProperty); }
            set { SetValue(PlayListProperty, value); }
        }

        private void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsManualSelect) _ = Songs[SongContainer.SelectedIndex].AppendMe();
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            _ = Songs[int.Parse((sender as Button).Tag.ToString())].AppendMe();
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse((sender as Button).Tag.ToString());
            (((sender as Button).Parent as StackPanel).Parent as Grid).ContextFlyout.ShowAt(sender as Button);
            IsManualSelect = true;
        }

        private async void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
        {
            var ncsong = Songs[SongContainer.SelectedIndex];
            var oldLoadList = ncsong.LoadList;
            ncsong.LoadList = false;
            await ncsong.AppendMe();
            ncsong.LoadList = oldLoadList;
        }

        private async void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
        {
            _ = await HyPlayList.AppendNCSong(Songs[SongContainer.SelectedIndex], HyPlayList.NowPlaying + 1);
            HyPlayList.SongAppendDone();
        }

        private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
        {
            if (Songs[SongContainer.SelectedIndex].Artist[0].Type == HyPlayItemType.Radio)
            {
                Common.NavigatePage(typeof(Me), Songs[SongContainer.SelectedIndex].Artist[0].id);
            }
            else
            {
                if (Songs[SongContainer.SelectedIndex].Artist.Count > 1)
                    await new ArtistSelectDialog(Songs[SongContainer.SelectedIndex].Artist).ShowAsync();
                else
                    Common.NavigatePage(typeof(ArtistPage), Songs[SongContainer.SelectedIndex].Artist[0].id);
            }
        }

        private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(AlbumPage), Songs[SongContainer.SelectedIndex].Album);
        }

        private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Comments), "sg" + Songs[SongContainer.SelectedIndex].sid);
        }

        private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(Songs[SongContainer.SelectedIndex]);
        }

        private void BtnMV_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(MVPage), Songs[SongContainer.SelectedIndex]);
        }

        private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
        {
            await new SongListSelect(Songs[SongContainer.SelectedIndex].sid).ShowAsync();
        }

        private async void Btn_Del_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(async () =>
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
            {
                { "op" , "del" },
                {"pid",playList.plid },
                {"tracks" , Songs[SongContainer.SelectedIndex].sid }
            });
            });
        }

        private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = sender as Grid;
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse(element.Tag.ToString());
            element.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
            IsManualSelect = true;
        }
    }
}
