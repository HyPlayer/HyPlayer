﻿using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.FileProperties;
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

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ExpandedPlayer : Page, IDisposable
    {
        private int sclock = 0;
        private bool iscompact = false;
        private readonly bool loaded = false;
        public double showsize;
        private int lastlrcid = 0;
        public double LyricWidth { get; set; }

        private LyricItem lastitem;
        private int lastwidth;
        private List<LyricItem> LyricList = new List<LyricItem>();

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
            Current_SizeChanged(null, null);
            ToggleButtonSound.IsChecked = Common.ShowLyricSound;
            ToggleButtonTranslation.IsChecked = Common.ShowLyricTrans;
        }

        public void Dispose()
        {
            HyPlayList.OnLyricChange -= RefreshLyricTime;
            HyPlayList.OnPlayItemChange -= OnSongChange;
            HyPlayList.OnLyricLoaded -= HyPlayList_OnLyricLoaded;
            HyPlayList.OnPlayPositionChange -= HyPlayList_OnPlayPositionChange;
            if (Window.Current != null)
                Window.Current.SizeChanged -= Current_SizeChanged;
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
        }

        private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            int nowwidth = e is null ? (int) Window.Current.Bounds.Width : (int) e.Size.Width;
            if (lastwidth == nowwidth) return; //有些时候会莫名其妙不更改大小的情况引发这个
            lastwidth = nowwidth;
            if (nowwidth > 800)
            {
                LyricWidth = nowwidth * 0.4;
            }
            else
            {
                LyricWidth = nowwidth;
            }

            ImageAlbumContainer.Visibility =
                nowwidth >= 800 || nowwidth <= 300 ? Visibility.Visible : Visibility.Collapsed;
            showsize = Math.Max(nowwidth / 66, 16);


            Task.Run((() =>
            {
                Common.Invoke((() =>
                {
                    foreach (UIElement elm in LyricBox.Children)
                    {
                        if (elm is LyricItem li)
                        {
                            li.Width = LyricWidth;
                            li.RefreshFontSize();
                        }
                    }
                }));
            }));
        }


        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Common.PageExpandedPlayer = this;
            //ImageAlbumContainer.Visibility = Visibility.Collapsed;
            try
            {
                OnSongChange(HyPlayList.List[HyPlayList.NowPlaying]);
                LoadLyricsBox();
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

            LyricItem item = LyricList[HyPlayList.lyricpos];
            if (item == null) return;
            lastitem?.OnHind();
            item?.OnShow();
            lastitem = item;
            if (sclock > 0)
            {
                sclock--;
                return;
            }

            GeneralTransform transform = item?.TransformToVisual((UIElement) LyricBoxContainer.Content);
            Point? position = transform?.TransformPoint(new Point(0, 0));
            LyricBoxContainer.ChangeView(null, position?.Y - (LyricBoxContainer.ViewportHeight / 3), null, false);
        }

        public void LoadLyricsBox()
        {
            if (HyPlayList.NowPlayingItem == null) return;
            LyricBox.Children.Clear();
            double blanksize = (LyricBoxContainer.ViewportHeight / 2);
            if (double.IsNaN(blanksize) || blanksize == 0)
            {
                blanksize = Window.Current.Bounds.Height / 3;
            }

            LyricBox.Children.Add(new Grid() {Height = blanksize});
            if (HyPlayList.Lyrics.Count == 0)
            {
                LyricItem lrcitem = new LyricItem(SongLyric.PureSong)
                {
                    Width = LyricWidth
                };
                LyricBox.Children.Add(lrcitem);
            }
            else
            {
                foreach (SongLyric songLyric in HyPlayList.Lyrics)
                {
                    LyricItem lrcitem = new LyricItem(songLyric)
                    {
                        Width = LyricWidth
                    };
                    LyricBox.Children.Add(lrcitem);
                }
            }

            LyricBox.Children.Add(new Grid() {Height = blanksize});
            LyricList = LyricBox.Children.OfType<LyricItem>().ToList();
            lastlrcid = HyPlayList.NowPlayingItem.GetHashCode();
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    await Task.Delay(500);
                    RefreshLyricTime();
                }));
            }));
        }


        public void OnSongChange(HyPlayItem mpi)
        {
            if (mpi != null)
            {
                Common.Invoke((async () =>
                {
                    try
                    {
                        if (mpi.ItemType == HyPlayItemType.Local)
                        {
                            BitmapImage img = new BitmapImage();
                            await img.SetSourceAsync(
                                (await mpi.AudioInfo.LocalSongFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999)));
                            ImageAlbum.ImageSource = img;
                        }
                        else
                        {
                            ImageAlbum.ImageSource = new BitmapImage(new Uri(mpi.AudioInfo.Picture));
                        }

                        TextBlockSinger.Text = mpi.AudioInfo.Artist;
                        TextBlockSongTitle.Text = mpi.AudioInfo.SongName;
                        Background = new ImageBrush()
                            {ImageSource = ImageAlbum.ImageSource, Stretch = Stretch.UniformToFill};
                        ProgressBarPlayProg.Maximum = mpi.AudioInfo.LengthInMilliseconds;
                        SliderVolumn.Value = HyPlayList.Player.Volume * 100;

                        if (lastlrcid != HyPlayList.NowPlayingItem.GetHashCode())
                        {
                            //歌词加载中提示
                            double blanksize = (LyricBoxContainer.ViewportHeight / 2);
                            if (double.IsNaN(blanksize) || blanksize == 0)
                            {
                                blanksize = Window.Current.Bounds.Height / 3;
                            }

                            LyricBox.Children.Clear();
                            LyricBox.Children.Add(new Grid() {Height = blanksize});
                            LyricItem lrcitem = new LyricItem(SongLyric.LoadingLyric)
                            {
                                Width = LyricWidth
                            };
                            LyricList = new List<LyricItem>() {lrcitem};
                            LyricBox.Children.Add(lrcitem);
                            LyricBox.Children.Add(new Grid() {Height = blanksize});
                        }
                    }
                    catch (Exception)
                    {
                    }

                    ;
                }));
            }
        }

        public void StartExpandAnimation()
        {
            Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    ImageAlbumContainer.Visibility = Visibility.Visible;
                    TextBlockSinger.Visibility = Visibility.Visible;
                    TextBlockSongTitle.Visibility = Visibility.Visible;
                    ConnectedAnimation anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
                    ConnectedAnimation anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
                    ConnectedAnimation anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
                    anim3.Configuration = new DirectConnectedAnimationConfiguration();
                    if (anim2 != null)
                    {
                        anim2.Configuration = new DirectConnectedAnimationConfiguration();
                    }

                    anim1.Configuration = new DirectConnectedAnimationConfiguration();
                    try
                    {
                        anim3?.TryStart(TextBlockSinger);
                        anim1?.TryStart(TextBlockSongTitle);
                        anim2?.TryStart(ImageAlbumContainer);
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
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
                if (ImageAlbumContainer.Visibility == Visibility.Visible)
                {
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbumContainer);
                }

                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
            }
            catch
            {
                //ignore
            }
        }

        private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            sclock = 10;
        }

        private void ToggleWindowShowMode(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (!iscompact)
            {
                Task.Run(() => { Common.Invoke((() => { Current_SizeChanged(null, null); })); });
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);
                iscompact = true;
                ToggleButtonTranslation.Visibility = Visibility.Collapsed;
                ToggleButtonSound.Visibility = Visibility.Collapsed;
            }
            else
            {
                Task.Run(() => { Common.Invoke((() => { Current_SizeChanged(null, null); })); });
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
                ImageAlbumContainer.Visibility =
                    Window.Current.Bounds.Width >= 800 ? Visibility.Visible : Visibility.Collapsed;
                ToggleButtonTranslation.Visibility = Visibility.Visible;
                ToggleButtonSound.Visibility = Visibility.Visible;
                iscompact = false;
            }
        }

        private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.isPlaying)
            {
                HyPlayList.Player.Pause();
            }
            else
            {
                HyPlayList.Player.Play();
            }

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
            if (Window.Current.Bounds.Width <= 300)
            {
                //小窗模式
                ImageAlbumContainer.Visibility = Visibility.Collapsed;
                StackPanelTiny.Visibility = Visibility.Visible;
            }
        }

        private void ExpandedPlayer_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Width <= 300)
            {
                //小窗模式
                ImageAlbumContainer.Visibility = Visibility.Visible;
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

        private async void TextBlockSinger_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (HyPlayList.NowPlayingItem.isOnline)
                {
                    if (HyPlayList.NowPlayingItem.NcPlayItem.Artist.Count > 1)
                    {
                        await new ArtistSelectDialog(HyPlayList.NowPlayingItem.NcPlayItem.Artist).ShowAsync();
                    }
                    else
                    {
                        Common.BaseFrame.Navigate(typeof(ArtistPage),
                            HyPlayList.NowPlayingItem.NcPlayItem.Artist[0].id);
                    }

                    Common.BarPlayBar.ButtonCollapse_OnClick(this, e);
                }
            }
            catch
            {
            }
        }

        private void ShowContent_Click()
        {
        }
    }
}