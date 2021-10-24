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
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

#endregion

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace HyPlayer.Controls
{
    public sealed partial class SongsList : UserControl, IDisposable
    {
        public bool MultiSelect
        {
            get { return (bool)GetValue(MultiSelectProperty); }
            set
            {
                SetValue(MultiSelectProperty,
                    value); /*SongContainer.SelectionMode = (value? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single);*/
            }
        }

        // Using a DependencyProperty as the backing store for MultiSelect.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MultiSelectProperty =
            DependencyProperty.Register("MultiSelect", typeof(bool), typeof(SongsList), new PropertyMetadata(false));


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

        public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
            "ListHeader", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

        public UIElement ListHeader
        {
            get => (UIElement)GetValue(ListHeaderProperty);
            set
            {
                HeaderPanel.Padding = new Thickness(0, 0, 0, 25);
                SetValue(ListHeaderProperty, value);
            }
        }

        public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
            "Footer", typeof(UIElement), typeof(SongsList), new PropertyMetadata(default(UIElement)));

        public UIElement Footer
        {
            get => (UIElement)GetValue(FooterProperty);
            set => SetValue(FooterProperty, value);
        }

        public bool IsManualSelect = true;

        private readonly ObservableCollection<NCSong> VisibleSongs = new ObservableCollection<NCSong>();

        public SongsList()
        {
            InitializeComponent();
            HyPlayList.OnPlayItemChange += HyPlayListOnOnPlayItemChange;
            MultiSelect = false;
            
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
            set
            {
                if (value)
                    HeaderPanel.Padding = new Thickness(0, 0, 0, 25);
                SetValue(IsSearchEnabledProperty, value);
            }
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
            if (SongContainer.SelectionMode == ListViewSelectionMode.Multiple) return;
            if (!IsManualSelect) return;
            if (VisibleSongs[SongContainer.SelectedIndex].sid == HyPlayList.NowPlayingItem.PlayItem?.Id) return;
            if (ListSource != null && ListSource != "content" && Songs.Count == VisibleSongs.Count)
            {
                HyPlayList.RemoveAllSong();
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
            Grid_RightTapped(((StackPanel)((Button)sender)?.Parent)?.Parent, null);
        }

        private void FlyoutItemPlay_Click(object sender, RoutedEventArgs e)
        {
            int origidx = HyPlayList.NowPlaying + 1;
            foreach (NCSong ncsong in SongContainer.SelectedItems)
                _ = HyPlayList.AppendNcSong(ncsong);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(origidx);
        }

        private void FlyoutItemPlayNext_Click(object sender, RoutedEventArgs e)
        {
            foreach (NCSong ncsong in SongContainer.SelectedItems)
                _ = HyPlayList.AppendNcSong(ncsong);
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
            foreach (NCSong ncsong in SongContainer.SelectedItems)
                DownloadManager.AddDownload(ncsong);
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
            if (SongContainer.SelectionMode == ListViewSelectionMode.Single)
            {
                IsManualSelect = false;
                SongContainer.SelectedIndex = int.Parse(element.Tag.ToString());
                IsManualSelect = true;
            }

            SongContainer.ContextFlyout.ShowAt(element,
                new FlyoutShowOptions { Position = e?.GetPosition(element) ?? new Point(element?.ActualWidth ?? 0, 0) });
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

    public class SongListSelectModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value != null)
                return (bool)value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
            return SelectionMode.Single;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}