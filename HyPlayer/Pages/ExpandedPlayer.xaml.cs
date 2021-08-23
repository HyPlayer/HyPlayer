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
using Windows.ApplicationModel.DataTransfer;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.UI;

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
        private bool ManualChangeMode = false;

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
        private bool programClick = false;
        public bool jumpedLyrics = false;

        public string lastSongUrlForBrush = "";
        public SolidColorBrush ForegroundAlbumBrush = Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush;


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
            ImageAlbum.ImageExOpened += async (a, b) =>
            {
                if (await IsBrightAsync())
                {
                    ForegroundAlbumBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    TextBlockSongTitle.Foreground = ForegroundAlbumBrush;
                }
                else
                {
                    ForegroundAlbumBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    TextBlockSongTitle.Foreground = ForegroundAlbumBrush;
                }
            };
            Current_SizeChanged(null, null);
            ToggleButtonSound.IsChecked = Common.ShowLyricSound;
            ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
            AlbumDropShadow.ShadowOpacity = (double)Common.Setting.expandedCoverShadowDepth / 10;
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
            Common.Invoke(() =>
            {
                HyPlayList.OnLyricChange -= RefreshLyricTime;
                HyPlayList.OnPlayItemChange -= OnSongChange;
                HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
                HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
                HyPlayList.OnTimerTicked -= HyPlayList_OnTimerTicked;
                ImageAlbum.Source = null;
                ImageAlbum.PlaceholderSource = null;
                Background = null;
                if (Window.Current != null)
                    Window.Current.SizeChanged -= Current_SizeChanged;
                Common.Invoke(() =>
                {
                    LyricList.Clear();
                });
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
                showsize = Common.Setting.lyricSize <= 0 ? Math.Max(nowwidth / 66, iscompact ? 16 : 23) : Common.Setting.lyricSize;

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
                    UIAugmentationSys.Visibility = Visibility.Visible;
                    LyricBox.Margin = new Thickness(0);
                    UIAugmentationSys.SetValue(Grid.ColumnProperty, 0);
                    UIAugmentationSys.SetValue(Grid.ColumnSpanProperty, 1);
                    RightPanel.SetValue(Grid.ColumnProperty, 1);
                    RightPanel.SetValue(Grid.ColumnSpanProperty, 1);
                    break;
                case ExpandedWindowMode.CoverOnly:
                    BtnToggleAlbum.IsChecked = true;
                    BtnToggleLyric.IsChecked = false;
                    UIAugmentationSys.Visibility = Visibility.Visible;
                    RightPanel.Visibility = Visibility.Collapsed;
                    UIAugmentationSys.SetValue(Grid.ColumnProperty, 0);
                    UIAugmentationSys.SetValue(Grid.ColumnSpanProperty, 2);
                    UIAugmentationSys.VerticalAlignment = VerticalAlignment.Stretch;
                    UIAugmentationSys.HorizontalAlignment = HorizontalAlignment.Stretch;
                    break;
                case ExpandedWindowMode.LyricOnly:
                    BtnToggleAlbum.IsChecked = false;
                    BtnToggleLyric.IsChecked = true;
                    RightPanel.Visibility = Visibility.Visible;
                    UIAugmentationSys.Visibility = Visibility.Collapsed;
                    RightPanel.SetValue(Grid.ColumnProperty, 0);
                    RightPanel.SetValue(Grid.ColumnSpanProperty, 2);
                    LyricBox.Margin = new Thickness(15);
                    LyricBoxContainer.Height = AlbumDropShadow.ActualHeight + 170;
                    break;
                default:
                    break;
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
                /*
                ImageAlbum.Width = Math.Max(Math.Min(MainGrid.ActualHeight, LeftPanel.ActualWidth) - 80, 1);
                ImageAlbum.Height = ImageAlbum.Width;
                */

                /*
                if (ImageAlbum.Width < 250 || iscompact)
                    SongInfo.Visibility = Visibility.Collapsed;
                else
                    SongInfo.Visibility = Visibility.Visible;
                */
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


            if (iscompact)
            {
                AlbumDropShadow.Width = double.NaN;
                LeftPanel.HorizontalAlignment = HorizontalAlignment.Left;
                AlbumDropShadow.HorizontalAlignment = HorizontalAlignment.Left;
            }
            else
            {
                if (550 > nowwidth)
                {
                    AlbumDropShadow.Width = nowwidth - AlbumDropShadow.ActualOffset.X - 15;
                    LeftPanel.HorizontalAlignment = HorizontalAlignment.Left;
                    AlbumDropShadow.HorizontalAlignment = HorizontalAlignment.Left;
                }
                else
                {
                    AlbumDropShadow.Width = double.NaN;
                    LeftPanel.HorizontalAlignment = HorizontalAlignment.Center;
                    AlbumDropShadow.HorizontalAlignment = HorizontalAlignment.Center;
                }
            }



            if (SongInfo.ActualOffset.Y + SongInfo.ActualHeight > MainGrid.ActualHeight && WindowMode != ExpandedWindowMode.LyricOnly)
            {
                float? size = (float?)(MainGrid.ActualHeight / (SongInfo.ActualOffset.Y + SongInfo.ActualHeight));
                UIAugmentationSys.ChangeView(0, 0, size);
            }
            else
            {
                UIAugmentationSys.ChangeView(0, 0, 1);
            }


            //{//合并显示
            //    SongInfo.SetValue(Grid.RowProperty, 1);
            //    SongInfo.VerticalAlignment = VerticalAlignment.Bottom;
            //    SongInfo.Background = Application.Current.Resources["ExpandedPlayerMask"] as Brush;
            //}
            //else
            //{
            //    SongInfo.SetValue(Grid.RowProperty, 2);
            //    SongInfo.VerticalAlignment = VerticalAlignment.Top;
            //    SongInfo.Background = null;
            //}

            if ((nowwidth <= 300 || nowheight <= 300) && iscompact && StackPanelTiny.Visibility == Visibility.Collapsed)
            {
                ImageAlbum.Visibility = Visibility.Collapsed;
                PageContainer.Background = null;
            }
            else
            {
                ImageAlbum.Visibility = Visibility.Visible;
                PageContainer.Background = Application.Current.Resources["ExpandedPlayerMask"] as AcrylicBrush;
            }

            lastChangedLyricWidth = LyricWidth;

            //歌词宽度
            if (nowwidth <= 800)
            {
                if (!ManualChangeMode && WindowMode == ExpandedWindowMode.Both)
                {
                    WindowMode = ExpandedWindowMode.CoverOnly;
                    ChangeWindowMode();
                }
            }
            else if (nowwidth > 800)
            {
                if (!ManualChangeMode && WindowMode != ExpandedWindowMode.Both)
                {
                    WindowMode = ExpandedWindowMode.Both;
                    ChangeWindowMode();
                }

            }
            LyricList.ForEach(t =>
            {
                t.Width = LyricWidth;
                t.RefreshFontSize();
            });
        }



        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay)
                await ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
            if (ApplicationView.GetForCurrentView().IsFullScreenMode)
                ApplicationView.GetForCurrentView().ExitFullScreenMode();
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
            programClick = true;
            BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
            BtnToggleTinyMode.IsChecked = ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay;
            programClick = false;
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
            var position = transform?.TransformPoint(new Windows.Foundation.Point(0, 0));
            LyricBoxContainer.ChangeView(null, position?.Y - MainGrid.ActualHeight / 4, null, false);

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
                                await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999));
                            ImageAlbum.Source = img;
                        }
                        else
                        {
                            ImageAlbum.PlaceholderSource = new BitmapImage(new Uri(mpi.PlayItem.Album.cover + "?param=" +
                                StaticSource.PICSIZE_EXPANDEDPLAYER_PREVIEWALBUMCOVER));
                            ImageAlbum.Source = new BitmapImage(new Uri(mpi.PlayItem.Album.cover));
                        }
                        TextBlockSinger.Content = mpi.PlayItem.ArtistString;
                        TextBlockSongTitle.Text = mpi.PlayItem.Name;
                        TextBlockAlbum.Content = mpi.PlayItem.AlbumString;
                        Background = new ImageBrush
                        { ImageSource = (ImageSource)ImageAlbum.Source, Stretch = Stretch.UniformToFill };
                        ProgressBarPlayProg.Maximum = mpi.PlayItem.LengthInMilliseconds;
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

        public void ExpandedPlayer_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if ((nowwidth <= 300 || nowheight <= 300) && iscompact)
            {
                //小窗模式
                StackPanelTiny.Visibility = Visibility.Visible;
                PageContainer.Background = Application.Current.Resources["ExpandedPlayerMask"] as AcrylicBrush;

            }
        }

        public void ExpandedPlayer_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if ((nowwidth <= 250 || nowheight <= 250) && iscompact)
            {
                //小窗模式
                StackPanelTiny.Visibility = Visibility.Collapsed;
                PageContainer.Background = null;

            }
        }

        private void ToggleButtonTranslation_OnClick(object sender, RoutedEventArgs e)
        {
            Common.ShowLyricTrans = ToggleButtonTranslation.IsChecked;
            LoadLyricsBox();
        }

        private void ToggleButtonSound_OnClick(object sender, RoutedEventArgs e)
        {
            Common.ShowLyricSound = ToggleButtonSound.IsChecked;
            LoadLyricsBox();
        }

        private void TextBlockAlbum_OnTapped(object sender, RoutedEventArgs e)
        {
            try
            {
                if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
                {
                    if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                    {
                        Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
                    }
                    else
                    {
                        if (HyPlayList.NowPlayingItem.PlayItem.Album.id != "0")
                            Common.NavigatePage(typeof(AlbumPage),
                                HyPlayList.NowPlayingItem.PlayItem.Album.id);
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
                    if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                    {
                        Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
                    }
                    else
                    {
                        if (HyPlayList.NowPlayingItem.PlayItem.Artist.Count > 1)
                        {
                            await new ArtistSelectDialog(HyPlayList.NowPlayingItem.PlayItem.Artist).ShowAsync(); return;
                        }
                        else
                            Common.NavigatePage(typeof(ArtistPage),
                                HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
                    }
                    Common.NavigatePage(typeof(BlankPage));
                    Common.BarPlayBar.ButtonCollapse_OnClick(this, null);
                }
            }
            catch
            {
            }
        }


        private async void SaveAlbumImage_Click(object sender, RoutedEventArgs e)
        {
            var filepicker = new FileSavePicker();
            filepicker.SuggestedFileName = HyPlayList.NowPlayingItem.PlayItem.Name + "-cover.jpg";
            filepicker.FileTypeChoices.Add("图片文件", new List<string> { ".png", ".jpg" });
            var file = await filepicker.PickSaveFileAsync();
            var stream = await (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Local
                    ? RandomAccessStreamReference.CreateFromUri(
                        new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover))
                    : RandomAccessStreamReference.CreateFromStream(
                        await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(
                            ThumbnailMode.SingleItem, 9999)))
                .OpenReadAsync();
            var buffer = new Buffer((uint)stream.Size);
            await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
            await FileIO.WriteBufferAsync(file, buffer);
        }

        private void BtnToggleWindowsMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!realclick) return;
            ManualChangeMode = true;
            if (BtnToggleAlbum.IsChecked && BtnToggleLyric.IsChecked)
            {
                WindowMode = ExpandedWindowMode.Both;
            }
            else if (BtnToggleAlbum.IsChecked)
            {
                WindowMode = ExpandedWindowMode.CoverOnly;
            }
            else if (BtnToggleLyric.IsChecked)
            {
                WindowMode = ExpandedWindowMode.LyricOnly;
            }
            ChangeWindowMode();
        }

        private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
        {
            if (programClick) return;
            if (BtnToggleFullScreen.IsChecked)
            {
                if (BtnToggleTinyMode.IsChecked)
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

            if (BtnToggleTinyMode.IsChecked)
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

        private void CopySongName_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(TextBlockSongTitle.Text);
            Clipboard.SetContent(dataPackage);
        }

        private void LyricBoxContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            LyricBoxContainer.ContextFlyout.ShowAt(LyricBoxContainer);
        }

        private async void BtnLoadLocalLyric(object sender, RoutedEventArgs e)
        {
            FileOpenPicker fop = new FileOpenPicker();
            fop.FileTypeFilter.Add(".lrc");
            // register provider - by default encoding is not supported 
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            HyPlayList.Lyrics = Utils.ConvertPureLyric(await FileIO.ReadTextAsync(await fop.PickSingleFileAsync()));
            LoadLyricsBox();
        }

        private void ImageAlbum_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (WindowMode == ExpandedWindowMode.CoverOnly)
            {
                WindowMode = ExpandedWindowMode.LyricOnly;
                ChangeWindowMode();
            }
        }

        private async void LyricBox_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (WindowMode == ExpandedWindowMode.LyricOnly)
            {
                await Task.Delay(1000);
                if (!jumpedLyrics)
                {
                    WindowMode = ExpandedWindowMode.CoverOnly;
                    ChangeWindowMode();
                }
                else jumpedLyrics = false;
            }
        }

        private async Task<bool> IsBrightAsync()
        {
            if (Common.Setting.lyricColor != 0)
            {
                return Common.Setting.lyricColor == 2;
            }
            if (lastSongUrlForBrush == HyPlayList.NowPlayingItem.PlayItem.url) return ForegroundAlbumBrush.Color.R == 0;
            try
            {
                BitmapDecoder decoder;
                if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Local)
                {
                    decoder = await BitmapDecoder.CreateAsync(await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 1));
                }
                else
                {
                    decoder = await BitmapDecoder.CreateAsync(await RandomAccessStreamReference.CreateFromUri(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover + "?param=1y1")).OpenReadAsync());
                }
                var data = await decoder.GetPixelDataAsync();
                var bytes = data.DetachPixelData();
                System.Drawing.Color c = GetPixel(bytes, 0, 0, decoder.PixelWidth, decoder.PixelHeight);
                double Y = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                lastSongUrlForBrush = HyPlayList.NowPlayingItem.PlayItem.url;
                return Y >= 150;
            }
            catch
            {
                return ActualTheme == ElementTheme.Light;
            }
        }

        public static System.Drawing.Color GetPixel(byte[] pixels, int x, int y, uint width, uint height)
        {
            int i = x;
            int j = y;
            int k = (i * (int)width + j) * 3;
            var r = pixels[k + 0];
            var g = pixels[k + 1];
            var b = pixels[k + 2];
            return System.Drawing.Color.FromArgb(0, r, g, b);
        }
    }

    enum ExpandedWindowMode
    {
        Both,
        CoverOnly,
        LyricOnly
    }
}