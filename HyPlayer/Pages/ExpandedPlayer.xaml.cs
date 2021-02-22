using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
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

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ExpandedPlayer : Page
    {
        private Timer timer;
        private int sclock = 0;
        private bool iscompact = false;
        private bool loaded = false;

        public ExpandedPlayer()
        {
            this.InitializeComponent();
            SliderVolumn.Value = AudioPlayer.AudioMediaPlayer.Volume * 100;
            loaded = true;
            Common.PageExpandedPlayer = this;

            timer = new Timer((state =>
            {
                this.Invoke((RefreshLyricTime));
            }), null, 0, 100);

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Common.PageExpandedPlayer = this;
            ImageAlbum.Visibility = Visibility.Collapsed;
            TextBlockSinger.Visibility = Visibility.Collapsed;
            TextBlockSongTitle.Visibility = Visibility.Collapsed;
            OnSongChange(AudioPlayer.AudioMediaPlaybackList.CurrentItem);
            timer = new Timer((state =>
            {
                this.Invoke((RefreshLyricTime));
            }), null, 0, 100);
        }

        private void RefreshLyricTime()
        {
            LyricItem lastlrcitem = null;
            bool showed = false;
            foreach (UIElement lyricBoxChild in LyricBox.Children)
            {
                if (lyricBoxChild is LyricItem lrcitem)
                {
                    if (AudioPlayer.AudioMediaPlayer.PlaybackSession.Position < lrcitem.Lrc.LyricTime)
                    {
                        if (!showed)
                        {
                            lastlrcitem?.OnShow();
                            if (sclock > 0)
                            {
                                sclock--;
                                return;
                            }
                            var transform = lastlrcitem?.TransformToVisual((UIElement)LyricBoxContainer.Content);
                            var position = transform?.TransformPoint(new Point(0, 0));
                            LyricBoxContainer.ChangeView(null, position?.Y - (LyricBoxContainer.ViewportHeight / 3), null, false);
                            showed = true;
                        }
                        else
                        {
                            lrcitem.OnHind();
                        }
                    }
                    else
                    {
                        lrcitem.OnHind();
                    }
                    lastlrcitem = lrcitem;
                }
            }

            if (!showed && lastlrcitem != null)
            {
                lastlrcitem.OnShow();
                if (sclock > 0)
                {
                    sclock--;
                    return;
                }
                var transform = lastlrcitem.TransformToVisual((UIElement)LyricBoxContainer.Content);
                var position = transform.TransformPoint(new Point(0, 0));
                LyricBoxContainer.ChangeView(null, position.Y - (LyricBoxContainer.ViewportHeight / 3), null, false);
                showed = true;
            }

            //暂停按钮
            PlayStateIcon.Glyph = AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB4" : "\uEDB5";
            //播放进度
            ProgressBarPlayProg.Value = AudioPlayer.AudioMediaPlayer.PlaybackSession.Position.TotalMilliseconds;
        }

        public void LoadLyricsBox()
        {
            LyricBox.Children.Clear();
            double blanksize = (LyricBoxContainer.ViewportHeight / 2);
            if (double.IsNaN(blanksize) || blanksize == 0)
            {
                blanksize = Window.Current.Bounds.Height / 3;
            }
            LyricBox.Children.Add(new Grid() { Height = blanksize });
            if (AudioPlayer.Lyrics.Count == 0)
            {
                LyricItem lrcitem = new LyricItem(SongLyric.PureSong);
                LyricBox.Children.Add(lrcitem);
            }
            else
            {
                foreach (SongLyric songLyric in AudioPlayer.Lyrics)
                {
                    LyricItem lrcitem = new LyricItem(songLyric);
                    lrcitem.Margin = new Thickness(0, 10, 0, 10);
                    LyricBox.Children.Add(lrcitem);
                }
            }
            LyricBox.Children.Add(new Grid() { Height = blanksize });
        }

        public void OnSongChange(MediaPlaybackItem mpi)
        {
            if (mpi != null)
            {
                this.Invoke((() =>
                {
                    try
                    {
                        ImageAlbum.Source = AudioPlayer.AudioInfos[mpi]
                            .Picture;
                        TextBlockSinger.Text = AudioPlayer.AudioInfos[mpi]
                            .Artist;
                        TextBlockSongTitle.Text = AudioPlayer.AudioInfos[mpi]
                            .SongName;
                        this.Background = new ImageBrush() { ImageSource = ImageAlbum.Source };
                        ProgressBarPlayProg.Maximum = AudioPlayer.AudioInfos[mpi].LengthInMilliseconds;
                        SliderVolumn.Value = AudioPlayer.AudioMediaPlayer.Volume * 100;
                        LoadLyricsBox();
                    }
                    catch (Exception) { }
;
                }));
            }
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        public void StartExpandAnimation()
        {
            Task.Run(() =>
            {
                this.Invoke(() =>
                {
                    ImageAlbum.Visibility = Visibility.Visible;
                    TextBlockSinger.Visibility = Visibility.Visible;
                    TextBlockSongTitle.Visibility = Visibility.Visible;
                    var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
                    var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
                    var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
                    anim3.Configuration = new DirectConnectedAnimationConfiguration();
                    anim2.Configuration = new DirectConnectedAnimationConfiguration();
                    anim1.Configuration = new DirectConnectedAnimationConfiguration();
                    anim3?.TryStart(TextBlockSinger);
                    anim1?.TryStart(TextBlockSongTitle);
                    anim2?.TryStart(ImageAlbum);

                });
            });
        }

        public void StartCollapseAnimation()
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
        }

        private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            sclock = 30;
        }

        private void ImageAlbum_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (!iscompact)
            {
                Task.Run(() =>
                {
                    this.Invoke((() =>
                    {
                        foreach (UIElement lyricBoxChild in LyricBox.Children)
                        {
                            if (lyricBoxChild is LyricItem li)
                            {
                                li.Current_SizeChanged(null, null);
                            }
                        }
                    }));
                });
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.CompactOverlay);
                iscompact = true;
            }
            else
            {
                Task.Run(() =>
                {
                    this.Invoke((() =>
                    {
                        foreach (UIElement lyricBoxChild in LyricBox.Children)
                        {
                            if (lyricBoxChild is LyricItem li)
                            {
                                li.Current_SizeChanged(null, null);
                            }
                        }
                    }));
                });
                ImageAlbum.Visibility = Visibility.Visible;
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
                iscompact = false;
            }
        }

        private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                AudioPlayer.AudioMediaPlayer.Pause();
            else if (AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                AudioPlayer.AudioMediaPlayer.Play();
            PlayStateIcon.Glyph = AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB5" : "\uEDB4";

        }

        private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.AudioMediaPlaybackList.MoveNext();
        }

        private void BtnPreSong_OnClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.AudioMediaPlaybackList.MovePrevious();
        }

        private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (loaded)
                AudioPlayer.AudioMediaPlayer.Volume = SliderVolumn.Value / 100;
        }

        private void ExpandedPlayer_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Width <= 300)
            {//小窗模式
                ImageAlbum.Visibility = Visibility.Collapsed;
                StackPanelTiny.Visibility = Visibility.Visible;
            }
        }

        private void ExpandedPlayer_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Width <= 300)
            {//小窗模式
                ImageAlbum.Visibility = Visibility.Visible;
                StackPanelTiny.Visibility = Visibility.Collapsed;
            }
        }
    }
}
