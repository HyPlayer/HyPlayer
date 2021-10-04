#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls
{
    public sealed partial class SongsList : UserControl, IDisposable
    {
        public static readonly DependencyProperty IsSearchEnabledProperty = DependencyProperty.Register(
            "IsSearchEnabled", typeof(bool),
            typeof(SongsList),
            new PropertyMetadata(null)
        );

        public static readonly DependencyProperty SongsProperty = DependencyProperty.Register(
            "Songs", typeof(ObservableCollection<NCSong>),
            typeof(SongsList),
            new PropertyMetadata(null)
        );

        public static readonly DependencyProperty ListSourceProperty = DependencyProperty.Register(
            "ListSource", typeof(string),
            typeof(SongsList),
            new PropertyMetadata(null)
        );


        public static readonly DependencyProperty IsMySongListProperty = DependencyProperty.Register(
            "IsMySongList", typeof(bool)
            ,
            typeof(SongsList),
            new PropertyMetadata(null)
        );

        public bool IsManualSelect = true;

        private readonly ObservableCollection<NCSong> VisibleSongs = new ObservableCollection<NCSong>();

        public SongsList()
        {
            InitializeComponent();
            HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
            Task.Run((() =>
           {
               Common.Invoke(async () =>
               {
                   int tryCount = 5;
                   while (--tryCount > 0)
                   {
                       await Task.Delay(TimeSpan.FromSeconds(2));
                       try
                       {

                           HyPlayListOnOnPlayItemChange(HyPlayList.NowPlayingItem);
                           break;
                       }
                       catch (Exception e)
                       {
                           continue;
                       }
                   }
               });
           }));
        }

        private void HyPlayListOnOnPlayItemChange(HyPlayItem playitem)
        {
            if (playitem.ItemType == HyPlayItemType.Local || playitem.PlayItem == null) return;
            int idx = VisibleSongs.ToList().FindIndex(t => t.sid == playitem.PlayItem.Id);
            if (idx != -1)
            {
                IsManualSelect = false;
                SongContainer.SelectedIndex = idx;
                IsManualSelect = true;
            }

        }


        public bool IsMySongList
        {
            get => (bool)GetValue(IsMySongListProperty);
            set => SetValue(IsMySongListProperty, value);
        }

        public bool IsSearchEnabled
        {
            get => (bool)GetValue(IsSearchEnabledProperty);
            set => SetValue(IsSearchEnabledProperty, value);
        }

        public ObservableCollection<NCSong> Songs
        {
            get => (ObservableCollection<NCSong>)GetValue(SongsProperty);
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


        public string ListSource
        {
            get => (string)GetValue(ListSourceProperty);
            set => SetValue(ListSourceProperty, value);
        }

        private void Songs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                    if (item is NCSong ncsong)
                        VisibleSongs.Add(ncsong);
            }

            else VisibleSongs.Clear();
        }

        private async void SongContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SongContainer.SelectedIndex == -1) return;

            if (!IsManualSelect) return;
            if (VisibleSongs[SongContainer.SelectedIndex].sid == HyPlayList.NowPlayingItem.PlayItem.Id) return;
            if (ListSource != null && ListSource != "content" && Songs.Count == VisibleSongs.Count)
            {
                HyPlayList.List.Clear();
                HyPlayList.Player.Pause();
                await HyPlayList.AppendNcSource(ListSource);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t =>
                    t.PlayItem?.Id == VisibleSongs[SongContainer.SelectedIndex].sid));
            }
            //else if (ListSource == null)
            //{
            //    var ncsong = VisibleSongs[SongContainer.SelectedIndex];
            //    _ = HyPlayList.AppendNCSong(ncsong);
            //    HyPlayList.SongAppendDone();
            //    HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.id == ncsong.sid));
            //}
            else
            {
                HyPlayList.AppendNcSongs(VisibleSongs);
                HyPlayList.SongAppendDone();
                HyPlayList.SongMoveTo(SongContainer.SelectedIndex);
            }
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            var ncsong = VisibleSongs[int.Parse((sender as Button).Tag.ToString())];
            _ = HyPlayList.AppendNcSong(ncsong);
            HyPlayList.SongMoveTo(HyPlayList.List.FindIndex(t => t.PlayItem.Id == ncsong.sid));
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
            _ = HyPlayList.AppendNcSong(ncsong, HyPlayList.NowPlaying + 1);
        }

        private void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
        {
            HyPlayList.AppendNcSong(VisibleSongs[SongContainer.SelectedIndex], HyPlayList.NowPlaying + 1);
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

        private void Btn_Del_Click(object sender, RoutedEventArgs e)
        {
            Common.Invoke(() =>
            {
                Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistTracks, new Dictionary<string, object>
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
            int vpos = -1;
            for (int b = 0; b < VisibleSongs.Count; b++)
            {
                if (!Songs.Contains(VisibleSongs[b]))
                    VisibleSongs.RemoveAt(b);
            }

            for (int i = 0; i < Songs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(FilterBox.Text) || Filter(Songs[i]))
                {
                    vpos++;
                    if (!VisibleSongs.Contains(Songs[i]))
                    {
                        VisibleSongs.Insert(vpos, Songs[i]);
                    }
                }
                else
                {
                    VisibleSongs.Remove(Songs[i]);
                }
            }
        }

        private bool Filter(NCSong ncsong)
        {
            return ncsong.songname.ToLower().Contains(FilterBox.Text.ToLower()) ||
                   ncsong.ArtistString.ToLower().Contains(FilterBox.Text.ToLower()) ||
                   ncsong.Album.name.ToLower().Contains(FilterBox.Text.ToLower()) ||
                   (ncsong.transname ?? "").ToLower().Contains(FilterBox.Text.ToLower()) ||
                   (ncsong.alias ?? "").ToLower().Contains(FilterBox.Text.ToLower());
        }

        private GridLength GetSearchHeight(bool IsEnabled)
        {
            if (IsEnabled)
                return new GridLength(35);
            else return new GridLength(0);
        }

        public void Dispose()
        {
            HyPlayList.OnPlayItemChange -= HyPlayListOnOnPlayItemChange;
            VisibleSongs.Clear();
            Songs.Clear();
        }
    }
}