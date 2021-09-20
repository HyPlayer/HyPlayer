using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls
{
    public sealed partial class SongsList : UserControl
    {
        public SongsList()
        {
            this.InitializeComponent();
        }

        private void Songs_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            VisibleSongs.Clear();
            foreach (NCSong song in Songs)
            {
                VisibleSongs.Add(song);
            }
        }

        public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
    "IsSearchEnabled", typeof(bool)
    ,
    typeof(SongsList),
    new PropertyMetadata(null)
);
        public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
            "Songs", typeof(ObservableCollection<NCSong>)
            ,
            typeof(SongsList),
            new PropertyMetadata(null)
        );

        public static readonly DependencyProperty ListSourceProperty = DependencyProperty.Register(
            "ListSource", typeof(string)
            ,
            typeof(SongsList),
            new PropertyMetadata(null)
        );


        public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
            "IsMySongList", typeof(bool)
            ,
            typeof(SongsList),
            new PropertyMetadata(null)
        );

        public bool IsMySongList
        {
            get { return (bool)GetValue(IsMySongListProperty); }
            set { SetValue(IsMySongListProperty, value); }
        }

        public bool IsSearchEnabled
        {
            get { return (bool)GetValue(IsSearchEnabledProperty); }
            set { SetValue(IsSearchEnabledProperty, value); }
        }
        public ObservableCollection<NCSong> Songs
        {
            get { return (ObservableCollection<NCSong>)GetValue(SongsProperty); }
            set
            {
                SetValue(SongsProperty, value);
                try
                {
                    Songs.CollectionChanged -= Songs_CollectionChanged;
                }
                catch
                {

                }
                finally
                {
                    Songs.CollectionChanged += Songs_CollectionChanged;
                }
            }
        }

        private ObservableCollection<NCSong> VisibleSongs = new ObservableCollection<NCSong>();

        public bool IsManualSelect = true;

        public string ListSource
        {
            get { return (string)GetValue(ListSourceProperty); }
            set { SetValue(ListSourceProperty, value); }
        }

        private async void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SongContainer.SelectedIndex == -1) return;
            if (IsManualSelect && ListSource != null && ListSource != "content" && Songs.Count == VisibleSongs.Count)
            {
                HyPlayList.List.Clear();
                HyPlayList.Player.Pause();
                await HyPlayList.AppendNCSource(ListSource);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t =>
                    t.PlayItem?.id == VisibleSongs[SongContainer.SelectedIndex].sid));
            }
            else if (ListSource == null)
            {
                var ncsong = VisibleSongs[SongContainer.SelectedIndex];
                _ = HyPlayList.AppendNCSong(ncsong);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
            }
            else
            {
                await HyPlayList.AppendNCSongs(VisibleSongs);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(SongContainer.SelectedIndex);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            var ncsong = VisibleSongs[int.Parse((sender as Button).Tag.ToString())];
            _ = HyPlayList.AppendNCSong(ncsong);
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
            HyPlayList.SongAppendDone();
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse((sender as Button).Tag.ToString());
            (((sender as Button).Parent as StackPanel).Parent as Grid).ContextFlyout.ShowAt(sender as Button);
            IsManualSelect = true;
        }

        private void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
        {
            var ncsong = VisibleSongs[SongContainer.SelectedIndex];
            _ = HyPlayList.AppendNCSong(ncsong, HyPlayList.NowPlaying + 1);
        }

        private async void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
        {
            _ = await HyPlayList.AppendNCSong(VisibleSongs[SongContainer.SelectedIndex], HyPlayList.NowPlaying + 1);
            HyPlayList.SongAppendDone();
        }

        private async void FlyoutItemSinger_Click(object sender, RoutedEventArgs e)
        {
            if (VisibleSongs[SongContainer.SelectedIndex].Artist[0].Type == HyPlayItemType.Radio)
            {
                Common.NavigatePage(typeof(Me), VisibleSongs[SongContainer.SelectedIndex].Artist[0].id);
            }
            else
            {
                if (VisibleSongs[SongContainer.SelectedIndex].Artist.Count > 1)
                    await new ArtistSelectDialog(VisibleSongs[SongContainer.SelectedIndex].Artist).ShowAsync();
                else
                    Common.NavigatePage(typeof(ArtistPage), VisibleSongs[SongContainer.SelectedIndex].Artist[0].id);
            }
        }

        private void FlyoutItemAlbum_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(AlbumPage), VisibleSongs[SongContainer.SelectedIndex].Album);
        }

        private void FlyoutItemComments_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(Comments), "sg" + VisibleSongs[SongContainer.SelectedIndex].sid);
        }

        private void FlyoutItemDownload_Click(object sender, RoutedEventArgs e)
        {
            DownloadManager.AddDownload(VisibleSongs[SongContainer.SelectedIndex]);
        }

        private void BtnMV_Click(object sender, RoutedEventArgs e)
        {
            Common.NavigatePage(typeof(MVPage), VisibleSongs[SongContainer.SelectedIndex]);
        }

        private async void FlyoutCollection_Click(object sender, RoutedEventArgs e)
        {
            await new SongListSelect(VisibleSongs[SongContainer.SelectedIndex].sid).ShowAsync();
        }

        private async void Btn_Del_Click(object sender, RoutedEventArgs e)
        {
            Common.Invoke(async () =>
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>()
                {
                    { "op", "del" },
                    { "pid", ListSource.Substring(2, ListSource.Length - 2) },
                    { "tracks", VisibleSongs[SongContainer.SelectedIndex].sid }
                });
            });
            VisibleSongs.RemoveAt(SongContainer.SelectedIndex);
        }

        private void Grid_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var element = sender as Grid;
            IsManualSelect = false;
            SongContainer.SelectedIndex = int.Parse(element.Tag.ToString());
            element.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
            IsManualSelect = true;
        }

        public static Brush GetBrush(bool IsAvailable)
        {
            return IsAvailable
                ? (Brush)Application.Current.Resources["DefaultTextForegroundThemeBrush"]
                : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
        }

        private void FilterBox_OnTextChanged(object sender, RoutedEventArgs e)
        {
            VisibleSongs.Clear();
            foreach(NCSong song in Songs)
            {
                if (Filter(song))
                    VisibleSongs.Add(song);
            }
        }

        private bool Filter(NCSong ncsong)
        {
            return ncsong.songname.ToLower().Contains(FilterBox.Text.ToLower()) || ncsong.ArtistString.ToLower().Contains(FilterBox.Text.ToLower()) ||
                   ncsong.Album.name.ToLower().Contains(FilterBox.Text.ToLower()) || (ncsong.transname ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
                   (ncsong.alias ?? "").ToLower().Contains(FilterBox.Text.ToLower());
        }



    }
}