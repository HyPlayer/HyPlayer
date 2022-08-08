#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using Microsoft.Toolkit.Uwp.UI.Media;
using Buffer = Windows.Storage.Streams.Buffer;
using Color = System.Drawing.Color;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ExpandedPlayer : Page, IDisposable
{
    public static readonly DependencyProperty NowPlaybackSpeedProperty = DependencyProperty.Register(
        "NowPlaybackSpeed", typeof(string), typeof(ExpandedPlayer),
        new PropertyMetadata("x" + HyPlayList.Player.PlaybackSession.PlaybackRate));

    private readonly bool loaded;

    public SolidColorBrush ForegroundAccentTextBrush =
        Application.Current.Resources["SystemControlPageTextBaseHighBrush"] as SolidColorBrush;

    public SolidColorBrush ForegroundIdleTextBrush =
        Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush;


    public bool jumpedLyrics;
    public double lastChangedLyricWidth;
    private int lastheight;

    private LyricItemModel lastitem;
    private int lastlrcid;

    public PlayItem lastSongForBrush;

    private int lastwidth;

    //private List<LyricItem> LyricList = new();
    private bool ManualChangeMode;
    private int needRedesign = 1;
    private int nowheight;
    private int nowwidth;
    private int offset;
    private bool programClick;
    private bool realclick;
    private int sclock;
    private ExpandedWindowMode WindowMode;


    public ExpandedPlayer()
    {
        InitializeComponent();
        loaded = true;
        Common.PageExpandedPlayer = this;
        HyPlayList.OnPause += HyPlayList_OnPause;
        HyPlayList.OnPlay += HyPlayList_OnPlay;
        HyPlayList.OnLyricChange += RefreshLyricTime;
        HyPlayList.OnPlayItemChange += OnSongChange;
        HyPlayList.OnLyricLoaded += HyPlayList_OnLyricLoaded;
        Window.Current.SizeChanged += Current_SizeChanged;
        HyPlayList.OnTimerTicked += HyPlayList_OnTimerTicked;
        Common.OnEnterForegroundFromBackground += () => OnSongChange(HyPlayList.NowPlayingItem);
    }

    public double showsize { get; set; }
    public double LyricWidth { get; set; }

    public string NowPlaybackSpeed
    {
        get => (string)GetValue(NowPlaybackSpeedProperty);
        set => SetValue(NowPlaybackSpeedProperty, value);
    }

    public void Dispose()
    {
        HyPlayList.OnLyricChange -= RefreshLyricTime;
        HyPlayList.OnPlayItemChange -= OnSongChange;
        HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
        HyPlayList.OnTimerTicked -= HyPlayList_OnTimerTicked;
        if (Window.Current != null)
            Window.Current.SizeChanged -= Current_SizeChanged;
        _ = Common.Invoke(() =>
        {
            ImageAlbum.Source = null;
            ImageAlbum.PlaceholderSource = null;
            Background = null;
            LyricBox.ItemsSource = null;
            if (Common.Setting.albumRotate)
                RotateAnimationSet.Stop();
            if (!Common.Setting.expandAlbumBreath) return;
            var ImageAlbumAni = Resources["ImageAlbumAni"] as Storyboard;
            ImageAlbumAni.Pause();
        });
    }

    private void HyPlayList_OnPlay()
    {
        Common.Invoke(() =>
        {
            if (Common.Setting.albumRotate)
                //网易云音乐圆形唱片
                RotateAnimationSet.StartAsync();
            if (Common.Setting.expandAlbumBreath)
            {
                var ImageAlbumAni = Resources["ImageAlbumAni"] as Storyboard;
                ImageAlbumAni.Begin();
            }
        });
    }

    private void HyPlayList_OnPause()
    {
        Common.Invoke(() =>
        {
            if (Common.Setting.albumRotate)
                RotateAnimationSet.Stop();
            if (Common.Setting.expandAlbumBreath)
            {
                var ImageAlbumAni = Resources["ImageAlbumAni"] as Storyboard;
                ImageAlbumAni.Pause();
            }
        });
    }

    private void HyPlayList_OnTimerTicked()
    {
        if (Common.IsInBackground) return;
        if (sclock > 0) sclock--;
        if (needRedesign > 0)
        {
            needRedesign--;
            Redesign();
        }
    }


    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        Dispose();
    }

    ~ExpandedPlayer()
    {
        Dispose();
    }

    private void HyPlayList_OnLyricLoaded()
    {
        Common.Invoke(LoadLyricsBox);
        needRedesign++;
    }

    private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        nowwidth = e is null ? (int)Window.Current.Bounds.Width : (int)e.Size.Width;
        nowheight = e is null ? (int)Window.Current.Bounds.Height : (int)e.Size.Height;
        if (lastwidth != nowwidth)
        {
            //这段不要放出去了
            if (nowwidth > 800 || WindowMode == ExpandedWindowMode.Both)
                LyricWidth = nowwidth * 0.4;
            else
                LyricWidth = nowwidth - 15;
            LyricWidth = Math.Max(LyricWidth, 0);
            showsize = Common.Setting.lyricSize <= 0
                ? Math.Max(nowwidth / 66, 23)
                : Common.Setting.lyricSize;

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
                break;
        }

        if (WindowMode == ExpandedWindowMode.LyricOnly)
            LyricWidth = nowwidth - 30;
        else
        {
            if (nowwidth > 800 || WindowMode == ExpandedWindowMode.Both)
                LyricWidth = nowwidth * 0.4;
            else
                LyricWidth = nowwidth - 30;
        }

        needRedesign++;
        realclick = true;
    }

    private void Redesign()
    {
        if (needRedesign > 5) needRedesign = 5;
        // 这个函数里面放无法用XAML实现的页面布局方式
        var lyricMargin = LyricBoxContainer.Margin;
        lyricMargin.Top = ImageAlbum.ActualOffset.Y;
        LyricBoxContainer.Margin = lyricMargin;


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
            ImageAlbum.Width = double.NaN;
            ImageAlbum.Height = double.NaN;
            SongInfo.Width = double.NaN;
        }

        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;

        /*
        if (550 > nowwidth)
        {
            ImageAlbum.Width = nowwidth - ImageAlbum.ActualOffset.X - 15;
            LeftPanel.HorizontalAlignment = HorizontalAlignment.Left;
            ImageAlbum.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            ImageAlbum.Width = double.NaN;
            LeftPanel.HorizontalAlignment = HorizontalAlignment.Center;
            ImageAlbum.HorizontalAlignment = HorizontalAlignment.Center;
        }
        */

        float sizey = 1;
        float sizex = 1;
        if (WindowMode != ExpandedWindowMode.LyricOnly)
        {
            if (SongInfo.ActualOffset.Y + SongInfo.ActualHeight > MainGrid.ActualHeight)
                sizey = (float)(MainGrid.ActualHeight / (SongInfo.ActualOffset.Y + SongInfo.ActualHeight));

            if (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth > LeftPanel.ActualWidth)
                sizex = (float)(LeftPanel.ActualWidth / (ImageAlbum.ActualOffset.X + ImageAlbum.ActualWidth));
            UIAugmentationSys.ChangeView(0, 0, Math.Min(sizex, sizey));
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

        /*
         // 小窗下的背景替换
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
        */
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

        LyricBox.Width = LyricWidth;

        //LyricList.ForEach(t =>
        //{
        //    t.Width = LyricWidth;
        //    t.RefreshFontSize();
        //});
    }


    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ApplicationView.GetForCurrentView().ViewMode == ApplicationViewMode.CompactOverlay)
            await ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
        if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
        Common.PageExpandedPlayer = null;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Common.IsInBackground = false;
        Common.PageExpandedPlayer = this;
        Window.Current.SetTitleBar(AppTitleBar);
        if (Common.Setting.lyricAlignment)
        {
            ToggleButtonTranslation.HorizontalAlignment = HorizontalAlignment.Left;
            ToggleButtonSound.HorizontalAlignment = HorizontalAlignment.Left;
        }

        Current_SizeChanged(null, null);
        //LeftPanel.Visibility = Visibility.Collapsed;
        programClick = true;
        BtnToggleFullScreen.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
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

        if (Common.Setting.expandedPlayerBackgroundType == 0 && !Common.Setting.expandedUseAcrylic)
            PageContainer.Background = new BackdropBlurBrush { Amount = 50.0 };

        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void RefreshLyricTime()
    {
        Common.Invoke(UpdateFocusingLyric);
    }

    private void UpdateFocusingLyric()
    {
        if (LyricBox.ItemsSource is not List<LyricItemModel> list ||
            HyPlayList.LyricPos < 0 || HyPlayList.LyricPos >= list.Count) return;
        if (HyPlayList.LyricPos == -1)
        {
            if (lastitem != null) lastitem.IsShow = false;

            LyricBoxContainer.ChangeView(null, 0, null, false);
        }

        var item = list[HyPlayList.LyricPos];
        if (item == null) return;

        if (lastitem != null) lastitem.IsShow = false;

        item.IsShow = true;
        lastitem = item;
        if (sclock > 0)
            return;

        var k = LyricBox.ItemsSourceView.IndexOf(item);

        if (k >= 0)
            try
            {
                var ele = LyricBox.GetOrCreateElement(k) as FrameworkElement;
                ele?.UpdateLayout();
                var transform = ele?.TransformToVisual((UIElement)LyricBoxContainer.Content);
                var position = transform?.TransformPoint(new Point(0, 0));

                if (position.HasValue)
                    LyricBoxContainer.ChangeView(null, position.Value.Y + MainGrid.ActualHeight / 8, null, false);
            }
            catch (Exception e)
            {
                // ignore
            }
    }

    public void LoadLyricsBox()
    {
        if (HyPlayList.NowPlayingItem == null) return;
        LyricBox.ItemsSource = null;
        var blanksize = LyricBoxContainer.ViewportHeight / 2;
        if (double.IsNaN(blanksize) || blanksize == 0) blanksize = Window.Current.Bounds.Height / 3;

        LyricBoxHost.Margin = new Thickness(0, blanksize, 0, blanksize);

        List<LyricItemModel> source = null;

        if (HyPlayList.Lyrics.Count == 0)
            source = new List<LyricItemModel>
            {
                new(SongLyric.PureSong)
            };
        else
            source = new List<LyricItemModel>(HyPlayList.Lyrics.Select(c => new LyricItemModel(c)));

        LyricBox.ItemsSource = source;
        LyricBox.Width = LyricWidth;
        lastlrcid = HyPlayList.NowPlayingItem.GetHashCode();
        InitLyricTime();
    }

    private async Task InitLyricTime()
    {
        await Task.Delay(500);
        RefreshLyricTime();
    }


    public void OnSongChange(HyPlayItem mpi)
    {
        Common.Invoke(() =>
        {
            TextBlockSinger.Content = mpi?.PlayItem?.ArtistString;
            TextBlockSongTitle.Text = mpi?.PlayItem?.Name;
            TextBlockAlbum.Content = mpi?.PlayItem?.AlbumString;

            if (mpi?.PlayItem == null)
            {
                //LyricList.Clear();
                //LyricBox.Children.Clear();
                LyricBox.ItemsSource = null;

                //LyricBox.Children.Add(new TextBlock() { Text = "当前暂无歌曲播放" });
                ImageAlbum.Source = null;
            }

            if (mpi?.PlayItem == null) return;

            async void LoadLyricColor()
            {
                if (await IsBrightAsync())
                {
                    ForegroundAccentTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    ForegroundIdleTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(114, 0, 0, 0));
                }
                else
                {
                    ForegroundAccentTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    ForegroundIdleTextBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(66, 255, 255, 255));
                }


                TextBlockSongTitle.Foreground = ForegroundAccentTextBrush;
                TextBlockSingerNameTip.Foreground = ForegroundIdleTextBrush;
                TextBlockAlbumNameTip.Foreground = ForegroundIdleTextBrush;
                TextBlockSinger.Foreground = ForegroundAccentTextBrush;
                TextBlockAlbum.Foreground = ForegroundAccentTextBrush;
                if (Common.Setting.playbarBackgroundElay)
                    Common.BarPlayBar.SetPlayBarIdleBackground(ForegroundIdleTextBrush);
                LoadLyricsBox();
            }

            async Task LoadCoverImage()
            {
                try
                {
                    if (mpi.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                    {
                        var storageFile = HyPlayList.NowPlayingStorageFile;
                        if (mpi.PlayItem.DontSetLocalStorageFile != null)
                            storageFile = mpi.PlayItem.DontSetLocalStorageFile;
                        var img = new BitmapImage();
                        await img.SetSourceAsync(
                            await storageFile.GetThumbnailAsync(ThumbnailMode.MusicView, 9999));

                        ImageAlbum.Source = img;
                        if (Common.Setting.expandedPlayerBackgroundType == 0)
                            Background = new ImageBrush
                                { ImageSource = (ImageSource)ImageAlbum.Source, Stretch = Stretch.UniformToFill };
                    }
                    else
                    {
                        ImageAlbum.PlaceholderSource = new BitmapImage(new Uri(mpi.PlayItem.Album.cover + "?param=" +
                                                                               StaticSource
                                                                                   .PICSIZE_EXPANDEDPLAYER_PREVIEWALBUMCOVER));
                        if (Common.Setting.expandedPlayerFullCover)
                            ImageAlbum.Source = new BitmapImage(new Uri(mpi.PlayItem.Album.cover));
                        else
                            ImageAlbum.Source = new BitmapImage(new Uri(mpi.PlayItem.Album.cover + "?param=" +
                                                                        StaticSource
                                                                            .PICSIZE_EXPANDEDPLAYER_COVER));

                        if (Common.Setting.expandedPlayerBackgroundType == 0)
                            Background = new ImageBrush
                                { ImageSource = (ImageSource)ImageAlbum.Source, Stretch = Stretch.UniformToFill };
                    }
                }
                catch (Exception)
                {
                }
            }

            if (lastlrcid != HyPlayList.NowPlayingItem.GetHashCode())
            {
                //歌词加载中提示
                var blanksize = LyricBoxContainer.ViewportHeight / 2;
                if (double.IsNaN(blanksize) || blanksize == 0) blanksize = Window.Current.Bounds.Height / 3;


                LyricBoxHost.Margin = new Thickness(0, blanksize, 0, blanksize);
                LyricBox.ItemsSource = new List<LyricItemModel>
                {
                    new(SongLyric.LoadingLyric)
                };
                LyricBox.Width = LyricWidth;
            }

            needRedesign++;
            LoadCoverImage();
            LoadLyricColor();
        });
    }

    public void StartExpandAnimation()
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
            anim1?.TryStart(TextBlockSongTitle);
            anim2?.TryStart(ImageAlbum);
        }
        catch
        {
            //ignore
        }
    }

    public void StartCollapseAnimation()
    {
        try
        {
            if (Common.Setting.expandAnimation &&
                Common.BarPlayBar.GridSongInfoContainer.Visibility == Visibility.Visible)
            {
                if (TextBlockSongTitle.ActualSize.X != 0 && TextBlockSongTitle.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
                if (ImageAlbum.ActualSize.X != 0 && ImageAlbum.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
                if (TextBlockSinger.ActualSize.X != 0 && TextBlockSinger.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
                if (TextBlockAlbum.ActualSize.X != 0 && TextBlockAlbum.ActualSize.Y != 0)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongAlbum", TextBlockAlbum);
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
        if (HyPlayList.IsPlaying)
            HyPlayList.Player.Pause();
        else
            HyPlayList.Player.Play();
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
            HyPlayList.PlayerOutgoingVolume = e.NewValue / 100;
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
                if (HyPlayList.NowPlayingItem.PlayItem.Album.id != "0")
                    Common.NavigatePage(typeof(AlbumPage),
                        HyPlayList.NowPlayingItem.PlayItem.Album.id);

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(RadioPage), HyPlayList.NowPlayingItem.PlayItem.Album.id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            Common.BarPlayBar.CollapseExpandedPlayer();
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
                if (HyPlayList.NowPlayingItem.PlayItem.Artist.Count > 1)
                {
                    await new ArtistSelectDialog(HyPlayList.NowPlayingItem.PlayItem.Artist).ShowAsync();
                    return;
                }

                Common.NavigatePage(typeof(ArtistPage),
                    HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
            }

            if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);

            if (Common.Setting.forceMemoryGarbage)
                Common.NavigatePage(typeof(BlankPage));
            Common.BarPlayBar.CollapseExpandedPlayer();
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
            WindowMode = ExpandedWindowMode.Both;
        else if (BtnToggleAlbum.IsChecked)
            WindowMode = ExpandedWindowMode.CoverOnly;
        else if (BtnToggleLyric.IsChecked) WindowMode = ExpandedWindowMode.LyricOnly;
        ChangeWindowMode();
    }

    private void BtnToggleFullScreen_Checked(object sender, RoutedEventArgs e)
    {
        if (programClick) return;
        if (BtnToggleFullScreen.IsChecked)
        {
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
    }

    private void CopySongName_Click(object sender, RoutedEventArgs e)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetText(TextBlockSongTitle.Text);
        Clipboard.SetContent(dataPackage);
    }

    private void LyricBoxContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        LyricBoxContainer.ContextFlyout.ShowAt(LyricBoxContainer);
    }

    private async void BtnLoadLocalLyric(object sender, RoutedEventArgs e)
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".lrc");
        // register provider - by default encoding is not supported 
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        HyPlayList.Lyrics = Utils.ConvertPureLyric(await FileIO.ReadTextAsync(await fop.PickSingleFileAsync()), true);
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
            else
            {
                jumpedLyrics = false;
            }
        }
    }

    private async Task<bool> IsBrightAsync()
    {
        if (Common.Setting.lyricColor != 0) return Common.Setting.lyricColor == 2;
        if (Common.Setting.expandedPlayerBackgroundType >= 2)
            // 强制颜色
            switch (Common.Setting.expandedPlayerBackgroundType)
            {
                case 2: //System
                    return Application.Current.RequestedTheme == ApplicationTheme.Light;
                    break;
                case 3: // White
                    return true;
                    break;
                case 4: // Black
                    return false;
                    break;
            }

        if (HyPlayList.NowPlayingItem.PlayItem == null) return false;

        if (lastSongForBrush == HyPlayList.NowPlayingItem.PlayItem) return ForegroundAccentTextBrush.Color.R == 0;
        try
        {
            BitmapDecoder decoder;
            if (HyPlayList.NowPlayingItem.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                decoder = await BitmapDecoder.CreateAsync(
                    await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.MusicView, 1));
            else
                decoder = await BitmapDecoder.CreateAsync(await RandomAccessStreamReference
                    .CreateFromUri(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover + "?param=1y1"))
                    .OpenReadAsync());
            var data = await decoder.GetPixelDataAsync();
            var bytes = data.DetachPixelData();
            //var c = GetPixel(bytes, 0, 0, decoder.PixelWidth, decoder.PixelHeight);
            var Y = 0.299 * bytes[2] + 0.587 * bytes[1] + 0.114 * bytes[0];
            lastSongForBrush = HyPlayList.NowPlayingItem.PlayItem;
            if (Common.Setting.expandedPlayerBackgroundType == 1)
                PageContainer.Background =
                    new SolidColorBrush(Windows.UI.Color.FromArgb(255, bytes[2], bytes[1], bytes[0]));
            return Y >= 150;
        }
        catch
        {
            return ActualTheme == ElementTheme.Light;
        }
    }

    public static Color GetPixel(byte[] pixels, int x, int y, uint width, uint height)
    {
        var i = x;
        var j = y;
        var k = (i * (int)width + j) * 3;
        var r = pixels[k + 0];
        var g = pixels[k + 1];
        var b = pixels[k + 2];
        return Color.FromArgb(0, r, g, b);
    }

    private void LyricOffsetAdd_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.FromMilliseconds(--offset * 100);
        TbOffset.Text = (HyPlayList.LyricOffset > TimeSpan.Zero ? "-" : "") +
                        HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void LyricOffsetMin_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.FromMilliseconds(++offset * 100);
        TbOffset.Text = (HyPlayList.LyricOffset > TimeSpan.Zero ? "-" : "") +
                        HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void LyricOffsetUnset_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.LyricOffset = TimeSpan.Zero;
        offset = 0;
        TbOffset.Text = (HyPlayList.LyricOffset < TimeSpan.Zero ? "-" : "") +
                        HyPlayList.LyricOffset.ToString("ss\\.ff");
    }

    private void BtnSpeedMinusClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.Player.PlaybackSession.PlaybackRate <= 0.2) return;
        HyPlayList.Player.PlaybackSession.PlaybackRate -= 0.1;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void BtnSpeedPlusClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.PlaybackRate += 0.1;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void TbNowSpeed_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        HyPlayList.Player.PlaybackSession.PlaybackRate = 1.0;
        NowPlaybackSpeed = "x" + HyPlayList.Player.PlaybackSession.PlaybackRate;
    }

    private void BtnCopyLyricClicked(object sender, RoutedEventArgs e)
    {
        new LyricShareDialog { Lyrics = HyPlayList.Lyrics }.ShowAsync();
    }

    private void BtnToggleTinyModeClick(object sender, RoutedEventArgs e)
    {
        ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);
        Common.PageMain.ExpandedPlayer.Navigate(typeof(CompactPlayerPage));
    }

    private void ChangeABRepeatStatus_Click(object sender, RoutedEventArgs e)
    {
        if (Common.ABRepeatStatus)
            HyPlayList.OnPlayPositionChange += HyPlayList.CheckABTimeRemaining;
        else
            HyPlayList.OnPlayPositionChange -= HyPlayList.CheckABTimeRemaining;
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.ABStartPoint = HyPlayList.Player.PlaybackSession.Position;
        ABStartPointItem.Text = Common.ABStartPointFriendlyValue;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.ABEndPoint = HyPlayList.Player.PlaybackSession.Position;
        ABEndPointItem.Text = Common.ABEndPointFriendlyValue;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ToggleButtonSound.IsChecked = Common.ShowLyricSound;
        ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
        if (Common.Setting.albumRound) ImageAlbum.CornerRadius = new CornerRadius(300);
        ImageAlbum.BorderThickness = new Thickness(Common.Setting.albumBorderLength);
        switch (Common.Setting.expandedPlayerBackgroundType)
        {
            case 0: // Default
            case 1: // According to Album
                break;
            case 2: // According to System
                PageContainer.Background = new SolidColorBrush(Colors.Transparent);
                break;
            case 3: // Force White
                PageContainer.Background = new SolidColorBrush(Colors.WhiteSmoke);
                break;
            case 4: // Force Black
                PageContainer.Background = new SolidColorBrush(Colors.Black);
                break;
        }

        if (Common.Setting.albumRotate)
            //网易云音乐圆形唱片
            if (HyPlayList.IsPlaying)
                RotateAnimationSet.StartAsync();
        if (Common.Setting.expandAlbumBreath)
        {
            var ImageAlbumAni = Resources["ImageAlbumAni"] as Storyboard;
            ImageAlbumAni.Begin();
        }
    }
}

public class AlbumShadowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return Common.Setting.albumRound || Common.Setting.expandAlbumBreath
            ? 0
            : (double)Common.Setting.expandedCoverShadowDepth / 10;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

internal enum ExpandedWindowMode
{
    Both,
    CoverOnly,
    LyricOnly
}