using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using Buffer = Windows.Storage.Streams.Buffer;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ExpandedPlayer : Page, IDisposable
    {
        private readonly bool loaded;
        private bool iscompact;
        public double lastChangedLyricWidth;
        private ExpandedWindowMode WindowMode;
        private bool HandChangeMode = false;

        private LyricItem lastitem;
        private int lastlrcid;
        private int lastwidth;
        private List<LyricItem> LyricList = new List<LyricItem>();
        private int sclock;
        private int nowwidth;
        private int nowheight;
        private int needRedesign = 1;
        private int lastheight;
        private bool realclick = false;

        public ExpandedPlayer()
        {
            InitializeComponent();
            SliderVolumn.Value = HyPlayList.Player.Volume * 100;
            loaded = true;
            Common.PageExpandedPlayer = this;
            HyPlayList.OnLyricChange += RefreshLyricTime;
            HyPlayList.OnPlayItemChange += OnSongChange;
            HyPlayList.OnLyricLoaded += HyPlayList_OnLyricLoaded;
            HyPlayList.OnPlayPositionChange += HyPlayList_OnPlayPositionChange;
            Window.Current.SizeChanged += Current_SizeChanged;
            HyPlayList.OnTimerTicked += HyPlayList_OnTimerTicked;
            Current_SizeChanged(null, null);
            ToggleButtonSound.IsChecked = Common.ShowLyricSound;
            ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
            if (!Common.Setting.lyricAlignment)
                LyricCtrlBtns.Width = 150;
        }

        private void HyPlayList_OnTimerTicked()
        {
            if (sclock > 0)
            {
                sclock--;
            }
            if (needRedesign > 0)
            {
                needRedesign--;
                Redesign();
            }
        }

        public double showsize { get; set; }
        public double LyricWidth { get; set; }


        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            LyricBox.Children.Clear();
            Dispose();
        }
        public void Dispose()
        {
            HyPlayList.OnLyricChange -= RefreshLyricTime;
            HyPlayList.OnPlayItemChange -= OnSongChange;
            HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
            HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
            HyPlayList.OnTimerTicked -= HyPlayList_OnTimerTicked;
            if (Window.Current != null)
                Window.Current.SizeChanged -= Current_SizeChanged;
            Common.Invoke(() =>
            {
                LyricList.Clear();
            });
        }

        ~ExpandedPlayer()
        {
            Dispose();
        }

        private void HyPlayList_OnPlayPositionChange(TimeSpan Position)
        {
            //暂停按钮
            PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB4" : "\uEDB5";
            //播放进度
            ProgressBarPlayProg.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
        }

        private void HyPlayList_OnLyricLoaded()
        {
            LoadLyricsBox();
            needRedesign++;
        }

        private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            nowwidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
            nowheight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
            if (lastwidth != nowwidth)
            {
                //这段不要放出去了
                if (nowwidth > 800)
                    LyricWidth = nowwidth * 0.4;
                else
                    LyricWidth = nowwidth - 15;
                LyricCtrlBtns.Width = LyricWidth;
                showsize = Common.Setting.lyricSize <= 0 ? Math.Max(nowwidth / 66, 16) : Common.Setting.lyricSize;

                lastwidth = nowwidth;
                needRedesign += 2;
            }
            else if (lastheight != nowheight)
            {
                lastheight = nowheight;
                needRedesign += 2;
            }
        }

        private void ChangeWindowMode()
        {
            realclick = false;
            needRedesign++;
            if (!iscompact)
            {
                StackPanelTiny.Visibility = Visibility.Collapsed;
                ImageAlbum.Visibility = Visibility.Visible;
            }
            else
            {
                SongInfo.Visibility = Visibility.Collapsed;
            }

            switch (WindowMode)
            {
                case ExpandedWindowMode.Both:
                    BtnToggleAlbum.IsChecked = true;
                    BtnToggleLyric.IsChecked = true;
                    RightPanel.Visibility = Visibility.Visible;
                    LeftPanel.Visibility = Visibility.Visible;
                    LyricBox.Margin = new Thickness(0);
                    LeftPanel.SetValue(Grid.ColumnProperty, 0);
                    LeftPanel.SetValue(Grid.ColumnSpanProperty, 1);
                    RightPanel.SetValue(Grid.ColumnProperty, 1);
                    RightPanel.SetValue(Grid.ColumnSpanProperty, 1);
                    ControlBtns.SetValue(Grid.ColumnProperty, 0);
                    ControlBtns.SetValue(Grid.ColumnSpanProperty, 1);
                    break;
                case ExpandedWindowMode.CoverOnly:
                    BtnToggleAlbum.IsChecked = true;
                    BtnToggleLyric.IsChecked = false;
                    LeftPanel.Visibility = Visibility.Visible;
                    RightPanel.Visibility = Visibility.Collapsed;
                    LeftPanel.SetValue(Grid.ColumnProperty, 0);
                    LeftPanel.SetValue(Grid.ColumnSpanProperty, 2);
                    ControlBtns.SetValue(Grid.ColumnProperty, 0);
                    ControlBtns.SetValue(Grid.ColumnSpanProperty, 2);
                    break;
                case ExpandedWindowMode.LyricOnly:
                    BtnToggleAlbum.IsChecked = false;
                    BtnToggleLyric.IsChecked = true;
                    RightPanel.Visibility = Visibility.Visible;
                    LeftPanel.Visibility = Visibility.Collapsed;
                    RightPanel.SetValue(Grid.ColumnProperty, 0);
                    RightPanel.SetValue(Grid.ColumnSpanProperty, 2);
                    LyricBox.Margin = new Thickness(15);
                    ControlBtns.SetValue(Grid.ColumnProperty, 0);
                    ControlBtns.SetValue(Grid.ColumnSpanProperty, 2);
                    LyricBoxContainer.Height = AlbumDropShadow.ActualHeight + 170;
                    break;
                default:
                    break;
            }
            if (nowwidth <= 800)
            {
                ControlBtns.SetValue(Grid.ColumnProperty, 0);
                ControlBtns.SetValue(Grid.ColumnSpanProperty, 2);
            }
            realclick = true;
        }

        private void Redesign()
        {
            if (needRedesign > 5) needRedesign = 5;
            // 这个函数里面放无法用XAML实现的页面布局方式
            var lyricMargin = LyricBoxContainer.Margin;
            lyricMargin.Top = AlbumDropShadow.ActualOffset.Y;
            LyricBoxContainer.Margin = lyricMargin;
            if (WindowMode == ExpandedWindowMode.Both)
                LyricBoxContainer.Height = SongInfo.ActualOffset.Y + 80;
            else if (iscompact)
                LyricBoxContainer.Height = Math.Max(RightPanel.ActualHeight, 101) - 100;
            else
                LyricBoxContainer.Height = RightPanel.ActualHeight;


            if (600 > Math.Min(LeftPanel.ActualHeight, MainGrid.ActualHeight))
            {
                ImageAlbum.Width = Math.Max(Math.Min(MainGrid.ActualHeight, LeftPanel.ActualWidth) - 80, 1);
                ImageAlbum.Height = ImageAlbum.Width;
                if (ImageAlbum.Width < 250 || iscompact)
                    SongInfo.Visibility = Visibility.Collapsed;
                else
                    SongInfo.Visibility = Visibility.Visible;
                SongInfo.Width = ImageAlbum.Width;
            }
            else
            {
                if (!iscompact)
                    SongInfo.Visibility = Visibility.Visible;
                ImageAlbum.Width = double.NaN;
                ImageAlbum.Height = double.NaN;
                SongInfo.Width = double.NaN;
            }

            if (AlbumDropShadow.ActualOffset.Y + AlbumDropShadow.ActualHeight + 190 > LeftPanel.ActualHeight)
            {//合并显示
                SongInfo.SetValue(Grid.RowProperty, 1);
                SongInfo.VerticalAlignment = VerticalAlignment.Bottom;
                SongInfo.Background = Application.Current.Resources["ExpandedPlayerMask"] as Brush;
            }
            else
            {
                SongInfo.SetValue(Grid.RowProperty, 2);
                SongInfo.VerticalAlignment = VerticalAlignment.Top;
                SongInfo.Background = null;
            }



            if (Common.Setting.lyricAlignment) LyricCtrlBtns.Width = LyricWidth;
            else LyricCtrlBtns.Width = 150;

            lastChangedLyricWidth = LyricWidth;

            //歌词宽度
            if (nowwidth <= 800)
            {
                if (!HandChangeMode && WindowMode == ExpandedWindowMode.Both)
                {
                    WindowMode = ExpandedWindowMode.CoverOnly;
                    ChangeWindowMode();
                }
                ControlBtns.SetValue(Grid.ColumnProperty, 0);
                ControlBtns.SetValue(Grid.ColumnSpanProperty, 2);
            }
            else if (nowwidth > 800)
            {
                if (!HandChangeMode && WindowMode != ExpandedWindowMode.Both)
                {
                    WindowMode = ExpandedWindowMode.Both;
                    ChangeWindowMode();
                }

            }
            LyricList.ForEach(t =>
            {
                t.RefreshFontSize();
                t.Width = LyricWidth;
            });
        }



        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Common.PageExpandedPlayer = this;
            Window.Current.SetTitleBar(AppTitleBar);
            if (Common.Setting.lyricAlignment)
            {
                ToggleButtonTranslation.HorizontalAlignment = HorizontalAlignment.Left;
                ToggleButtonSound.HorizontalAlignment = HorizontalAlignment.Left;
            }
            //LeftPanel.Visibility = Visibility.Collapsed;
            try
            {
                OnSongChange(HyPlayList.List[HyPlayList.NowPlaying]);
                LoadLyricsBox();
                ChangeWindowMode();
                needRedesign++;
            }
            catch
            {
            }
        }

        private void RefreshLyricTime()
        {
            if (HyPlayList.lyricpos < 0 || HyPlayList.lyricpos >= LyricList.Count) return;
            if (HyPlayList.lyricpos == -1)
            {
                lastitem?.OnHind();
                LyricBoxContainer.ChangeView(null, 0, null, false);
            }

            var item = LyricList[HyPlayList.lyricpos];
            if (item == null) return;
            lastitem?.OnHind();
            item?.OnShow();
            lastitem = item;
            if (sclock > 0)
                return;
            var transform = item?.TransformToVisual((UIElement)LyricBoxContainer.Content);
            var position = transform?.TransformPoint(new Point(0, 0));
            LyricBoxContainer.ChangeView(null, position?.Y - LyricBoxContainer.ViewportHeight / 5, null, false);
        }

        public void LoadLyricsBox()
        {
            if (HyPlayList.NowPlayingItem == null) return;
            LyricBox.Children.Clear();
            var blanksize = LyricBoxContainer.ViewportHeight / 2;
            if (double.IsNaN(blanksize) || blanksize == 0) blanksize = Window.Current.Bounds.Height / 3;

            LyricBox.Children.Add(new Grid { Height = blanksize });
            if (HyPlayList.Lyrics.Count == 0)
            {
                var lrcitem = new LyricItem(SongLyric.PureSong)
                {
                    Width = LyricWidth
                };
                LyricBox.Children.Add(lrcitem);
            }
            else
            {
                foreach (var songLyric in HyPlayList.Lyrics)
                {
                    var lrcitem = new LyricItem(songLyric)
                    {
                        Width = LyricWidth
                    };
                    LyricBox.Children.Add(lrcitem);
                }
            }

            LyricBox.Children.Add(new Grid { Height = blanksize });
            LyricList = LyricBox.Children.OfType<LyricItem>().ToList();
            lastlrcid = HyPlayList.NowPlayingItem.GetHashCode();
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    await Task.Delay(500);
                    RefreshLyricTime();
                });
            });
        }


        public void OnSongChange(HyPlayItem mpi)
        {
            if (mpi != null)
                Common.Invoke(async () =>
                {
                    try
                    {
                        if (mpi.ItemType == HyPlayItemType.Local)
                        {
                            var img = new BitmapImage();
                            await img.SetSourceAsync(
                                await mpi.AudioInfo.LocalSongFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999));
                            ImageAlbum.Source = img;
                        }
                        else
                        {
                            ImageAlbum.PlaceholderSource = new BitmapImage(new Uri(mpi.AudioInfo.Picture + "?param=" +
                                StaticSource.PICSIZE_EXPANDEDPLAYER_PREVIEWALBUMCOVER));
                            ImageAlbum.Source = new BitmapImage(new Uri(mpi.AudioInfo.Picture));
                        }

                        TextBlockSinger.Content = mpi.AudioInfo.Artist;
                        TextBlockSongTitle.Text = mpi.AudioInfo.SongName;
                        TextBlockAlbum.Content = mpi.AudioInfo.Album;
                        Background = new ImageBrush
                        { ImageSource = (ImageSource)ImageAlbum.Source, Stretch = Stretch.UniformToFill };
                        ProgressBarPlayProg.Maximum = mpi.AudioInfo.LengthInMilliseconds;
                        SliderVolumn.Value = HyPlayList.Player.Volume * 100;

                        if (lastlrcid != HyPlayList.NowPlayingItem.GetHashCode())
                        {
                            //歌词加载中提示
                            var blanksize = LyricBoxContainer.ViewportHeight / 2;
                            if (double.IsNaN(blanksize) || blanksize == 0) blanksize = Window.Current.Bounds.Height / 3;

                            LyricBox.Children.Clear();
                            LyricBox.Children.Add(new Grid { Height = blanksize });
                            var lrcitem = new LyricItem(SongLyric.LoadingLyric)
                            {
                                Width = LyricWidth
                            };
                            LyricList = new List<LyricItem> { lrcitem };
                            LyricBox.Children.Add(lrcitem);
                            LyricBox.Children.Add(new Grid { Height = blanksize });
                        }

                        needRedesign++;
                    }
                    catch (Exception)
                    {
                    }
                });
        }

        public void StartExpandAnimation()
        {
            Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    ImageAlbum.Visibility = Visibility.Visible;
                    TextBlockSinger.Visibility = Visibility.Visible;
                    TextBlockSongTitle.Visibility = Visibility.Visible;
                    var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
                    var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
                    var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
                    if (anim2 != null) anim3.Configuration = new DirectConnectedAnimationConfiguration();
                    if (anim2 != null) anim2.Configuration = new DirectConnectedAnimationConfiguration();
                    if (anim2 != null) anim1.Configuration = new DirectConnectedAnimationConfiguration();
                    try
                    {
                        //anim3?.TryStart(TextBlockSinger);
                        //anim1?.TryStart(TextBlockSongTitle);
                        anim2?.TryStart(ImageAlbum);
                    }
                    catch
                    {
                        //ignore
                    }
                });
            });
        }

        public void StartCollapseAnimation()
        {
            try
            {
                if (Common.Setting.expandAnimation &&
                    Common.BarPlayBar.GridSongInfoContainer.Visibility == Visibility.Visible)
                {
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
                    if (ImageAlbum.Visibility == Visibility.Visible)
                        ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);

                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
                }
            }
            catch
            {
                //ignore
            }
        }

        private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            sclock = 5;
        }

        private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.isPlaying)
                HyPlayList.Player.Pause();
            else
                HyPlayList.Player.Play();

            PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB5" : "\uEDB4";
        }

        private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.SongMoveNext();
        }

        private void BtnPreSong_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.SongMovePrevious();
        }

        private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (loaded)
            {
                Common.BarPlayBar.SliderAudioRate.Value = e.NewValue;
                HyPlayList.Player.Volume = e.NewValue / 100;
            }
        }

        private void ExpandedPlayer_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Height <= 300)
            {
                //小窗模式
                ImageAlbum.Visibility = Visibility.Collapsed;
                StackPanelTiny.Visibility = Visibility.Visible;
            }
        }

        private void ExpandedPlayer_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Height <= 300)
            {
                //小窗模式
                ImageAlbum.Visibility = Visibility.Visible;
                StackPanelTiny.Visibility = Visibility.Collapsed;
            }
        }

        private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
        {
            Common.ShowLyricTrans = ToggleButtonTranslation.IsChecked.Value;
            LoadLyricsBox();
        }

        private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
        {
            Common.ShowLyricSound = ToggleButtonSound.IsChecked.Value;
            LoadLyricsBox();
        }

        private void TextBlockAlbum_OnTapped(object sender, RoutedEventArgs e)
        {
            try
            {
                if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
                {
                    if (HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].Type == HyPlayItemType.Radio)
                    {
                        Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].id);
                    }
                    else
                    {
                        if (HyPlayList.NowPlayingItem.NcPlayItem.Album.id != "0")
                            Common.NavigatePage(typeof(AlbumPage),
                                HyPlayList.NowPlayingItem.NcPlayItem.Album.id);
                    }
                    Common.NavigatePage(typeof(BlankPage));
                    Common.BarPlayBar.ButtonCollapse_OnClick(this, null);
                }
            }
            catch
            {
            }
        }

        private async void TextBlockSinger_OnTapped(object sender, RoutedEventArgs tappedRoutedEventArgs)
        {
            try
            {
                if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
                {
                    if (HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].Type == HyPlayItemType.Radio)
                    {
                        Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].id);
                    }
                    else
                    {
                        if (HyPlayList.NowPlayingItem.NcPlayItem.Artist.Count > 1)
                            await new ArtistSelectDialog(HyPlayList.NowPlayingItem.NcPlayItem.Artist).ShowAsync();
                        else
                            Common.NavigatePage(typeof(ArtistPage),
                                HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].id);
                    }

                    Common.NavigatePage(typeof(BlankPage));
                    Common.BarPlayBar.ButtonCollapse_OnClick(this, null);
                }
            }
            catch
            {
            }
        }


        private void ImageAlbum_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private async void SaveAlbumImage_Click(object sender, RoutedEventArgs e)
        {
            var filepicker = new FileSavePicker();
            filepicker.SuggestedFileName = HyPlayList.NowPlayingItem.Name + "-cover.jpg";
            filepicker.FileTypeChoices.Add("图片文件", new List<string> { ".png", ".jpg" });
            var file = await filepicker.PickSaveFileAsync();
            var stream = await (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Local
                    ? RandomAccessStreamReference.CreateFromUri(
                        new Uri(HyPlayList.NowPlayingItem.NcPlayItem.Album.cover))
                    : RandomAccessStreamReference.CreateFromStream(
                        await HyPlayList.NowPlayingItem.AudioInfo.LocalSongFile.GetThumbnailAsync(
                            ThumbnailMode.SingleItem, 9999)))
                .OpenReadAsync();
            var buffer = new Buffer((uint)stream.Size);
            await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            await FileIO.WriteBufferAsync(file, buffer);
        }

        private void BtnToggleWindowsMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!realclick) return;
            HandChangeMode = true;
            if (BtnToggleAlbum.IsChecked.Value && BtnToggleLyric.IsChecked.Value)
            {
                WindowMode = ExpandedWindowMode.Both;
            }
            else if (BtnToggleAlbum.IsChecked.Value)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
            }
            else if (BtnToggleLyric.IsChecked.Value)
            {
                WindowMode = ExpandedWindowMode.LyricOnly;
            }
            ChangeWindowMode();
        }

        private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
        {
            if (BtnToggleFullScreen.IsChecked.Value)
            {
                if (BtnToggleTinyMode.IsChecked.Value)
                {
                    BtnToggleTinyMode.IsChecked = false;
                    WindowMode = ExpandedWindowMode.Both;
                }
                ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
                ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;
                ChangeWindowMode();
            }
            else if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            {
                ApplicationView.GetForCurrentView().ExitFullScreenMode();
                ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.Auto;
                ChangeWindowMode();
            }

            if (BtnToggleTinyMode.IsChecked.Value)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
                iscompact = true;
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);
                ChangeWindowMode();
            }
            else if (ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay)
            {
                iscompact = false;
                WindowMode = ExpandedWindowMode.Both;
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
                ChangeWindowMode();
            }
        }
    }

    enum ExpandedWindowMode
    {
        Both,
        CoverOnly,
        LyricOnly
    }
}