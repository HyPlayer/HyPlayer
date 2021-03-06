using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.DirectX;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.Composition;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
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
using AudioVisualizer;
using Microsoft.Graphics.Canvas.UI.Composition;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ExpandedPlayer : Page, IBarElementFactory
    {
        private int sclock = 0;
        private bool iscompact = false;
        private bool loaded = false;
        public double showsize;
        private SourceConverter _spectrumSource;
        private PlaybackSource _playbackSource;
        public double LyricWidth { get; set; }

        public ExpandedPlayer()
        {
            this.InitializeComponent();
            SliderVolumn.Value = HyPlayList.Player.Volume * 100;
            loaded = true;
            Common.PageExpandedPlayer = this;
            HyPlayList.OnPlayPositionChange += RefreshLyricTime;
            HyPlayList.OnPlayItemChange += OnSongChange;
            HyPlayList.OnLyricLoaded += HyPlayList_OnLyricLoaded;
            Window.Current.SizeChanged += Current_SizeChanged;
            Current_SizeChanged(null, null);
        }

        private void PlaybackSource_Changed(object sender, IVisualizationSource args)
        {
            _spectrumSource.Source = _playbackSource.Source;
            }
        private void HyPlayList_OnLyricLoaded()
        {
            LoadLyricsBox();
        }

        private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            if (e == null)
            {
                LyricWidth = Math.Max(Window.Current.Bounds.Width * 0.4, LyricBoxContainer.ViewportWidth);
                showsize = Math.Max((int)Window.Current.Bounds.Width / 70, 16);
            }
            else
            {
                if (e.Size.Width > 800)
                {
                    LyricWidth = e.Size.Width * 0.4;
                }
                else
                {
                    LyricWidth = Math.Max(e.Size.Width * 0.4, LyricBoxContainer.ViewportWidth);
                }

                showsize = Math.Max(e.Size.Width / 70, 16);
            }

            Task.Run((() =>
            {
                this.Invoke((() =>
                {
                    foreach (UIElement elm in LyricBox.Children)
                    {
                        if (elm is LyricItem li)
                            li.Width = LyricWidth;
                    }
                }));
            }));
        }

        public void LoadVisualizer()
        {
            Task.Run(( async () =>
            {
                await Task.Delay(2000);
                this.Invoke((() =>
                {
                    _playbackSource = PlaybackSource.CreateFromMediaPlayer(HyPlayList.Player);
                    _playbackSource.SourceChanged += PlaybackSource_Changed;
                    _spectrumSource = (SourceConverter) Resources["spectrumSource"];
                    _spectrumSource.SpectrumRiseTime = TimeSpan.FromMilliseconds(100);
                    _spectrumSource.SpectrumFallTime = TimeSpan.FromMilliseconds(200);
                    _spectrumSource.FrequencyCount = 50;
                    _spectrumSource.MinFrequency = 20.0f;
                    _spectrumSource.MaxFrequency = 20000.0f;
                    _spectrumSource.FrequencyScale = ScaleType.Logarithmic;
                    _spectrumSource.AnalyzerTypes = AnalyzerType.Spectrum;
                    barspectrum.ElementFactory = this;
                    barspectrum.ElementShadowBlurRadius = 5;
                    barspectrum.ElementShadowOffset = new Vector3(2, 2, -10);
                    barspectrum.ElementShadowColor = Colors.LightGreen;
                    _spectrumSource.Source = _playbackSource.Source;
                }));
            }));

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Common.PageExpandedPlayer = this;
            ImageAlbumContainer.Visibility = Visibility.Collapsed;
            TextBlockSinger.Visibility = Visibility.Collapsed;
            TextBlockSongTitle.Visibility = Visibility.Collapsed;
            try
            {
                LoadVisualizer();
                OnSongChange(HyPlayList.List[HyPlayList.NowPlaying]);
                LoadLyricsBox();
            }
            catch { }

        }

        private void RefreshLyricTime(TimeSpan nowtime)
        {
            LyricItem lastlrcitem = null;
            bool showed = false;
            foreach (UIElement lyricBoxChild in LyricBox.Children)
            {
                if (lyricBoxChild is LyricItem lrcitem)
                {
                    if (HyPlayList.Player.PlaybackSession.Position < lrcitem.Lrc.LyricTime)
                    {
                        if (!showed)
                        {
                            lastlrcitem?.OnShow();
                            lrcitem.OnHind();
                            if (sclock > 0)
                            {
                                sclock--;
                                return;
                            }

                            var transform = lastlrcitem?.TransformToVisual((UIElement)LyricBoxContainer.Content);
                            var position = transform?.TransformPoint(new Point(0, 0));
                            LyricBoxContainer.ChangeView(null, position?.Y - (LyricBoxContainer.ViewportHeight / 3), null, false); ;
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
            PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB4" : "\uEDB5";
            //播放进度
            ProgressBarPlayProg.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
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
            if (HyPlayList.Lyrics.Count == 0)
            {
                LyricItem lrcitem = new LyricItem(SongLyric.PureSong);
                lrcitem.Width = LyricWidth;
                LyricBox.Children.Add(lrcitem);
            }
            else
            {
                foreach (SongLyric songLyric in HyPlayList.Lyrics)
                {
                    LyricItem lrcitem = new LyricItem(songLyric);
                    lrcitem.Margin = new Thickness(0, 10, 0, 10);
                    lrcitem.Width = LyricWidth;
                    LyricBox.Children.Add(lrcitem);
                }
            }
            LyricBox.Children.Add(new Grid() { Height = blanksize });
        }



        public void OnSongChange(HyPlayItem mpi)
        {
            if (mpi != null)
            {
                this.Invoke((() =>
                {
                    try
                    {
                        ImageAlbum.Source = mpi.ItemType == HyPlayItemType.Local ? mpi.AudioInfo.BitmapImage : new BitmapImage(new Uri(mpi.AudioInfo.Picture));
                        TextBlockSinger.Text = mpi.AudioInfo.Artist;
                        TextBlockSongTitle.Text = mpi.AudioInfo.SongName;
                        this.Background = new ImageBrush() { ImageSource = ImageAlbum.Source , Stretch = Stretch.UniformToFill};
                        ProgressBarPlayProg.Maximum = mpi.AudioInfo.LengthInMilliseconds;
                        SliderVolumn.Value = HyPlayList.Player.Volume * 100;
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
                    ImageAlbumContainer.Visibility = Visibility.Visible;
                    TextBlockSinger.Visibility = Visibility.Visible;
                    TextBlockSongTitle.Visibility = Visibility.Visible;
                    var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
                    var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
                    var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
                    anim3.Configuration = new DirectConnectedAnimationConfiguration();
                    if (anim2 != null)
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
            if (ImageAlbumContainer.Visibility == Visibility.Visible)
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
        }

        private void LyricBoxContainer_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            sclock = 30;
        }

        private void ToggleWindowShowMode(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (!iscompact)
            {
                Task.Run(() =>
                {
                    this.Invoke((() =>
                    {
                        Current_SizeChanged(null, null);
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
                        Current_SizeChanged(null, null);
                    }));
                });
                _ = ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
                iscompact = false;
            }
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
            HyPlayList.PlaybackList.MoveNext();
        }

        private void BtnPreSong_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.PlaybackList.MovePrevious();
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
            {//小窗模式
                ImageAlbumContainer.Visibility = Visibility.Collapsed;
                StackPanelTiny.Visibility = Visibility.Visible;
            }
        }

        private void ExpandedPlayer_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Window.Current.Bounds.Width <= 300)
            {//小窗模式
                ImageAlbumContainer.Visibility = Visibility.Visible;
                StackPanelTiny.Visibility = Visibility.Collapsed;
            }
        }




        public CompositionBrush CreateElementBrush(object sender, Color elementColor, Size size, Compositor compositor, CompositionGraphicsDevice device)
        {
            if (size.Width == 0 && size.Height == 0)
                return null;

            var surface = device.CreateDrawingSurface(size, DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            using (var drawingSession = CanvasComposition.CreateDrawingSession(surface))
            {
                drawingSession.Clear(Colors.Transparent);
                var center = size.ToVector2() / 2.0f;
                var radius = center.X > center.Y ? center.Y : center.X;
                drawingSession.FillCircle(center, radius, elementColor);
            }
            return compositor.CreateSurfaceBrush(surface);
        }

        public CompositionBrush CreateShadowMask(object sender, Size size, Compositor compositor, CompositionGraphicsDevice device)
        {
            if (size.Width == 0 && size.Height == 0)
                return null;

            var surface = device.CreateDrawingSurface(size, DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            using (var drawingSession = CanvasComposition.CreateDrawingSession(surface))
            {
                drawingSession.Clear(Colors.Transparent);
                var center = size.ToVector2() / 2.0f;
                var radius = center.X > center.Y ? center.Y : center.X;
                drawingSession.FillCircle(center, radius, Color.FromArgb(255, 255, 255, 255));
            }
            return compositor.CreateSurfaceBrush(surface);
        }
    }
}
