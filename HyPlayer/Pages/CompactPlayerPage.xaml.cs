using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Services.Maps;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class CompactPlayerPage : Page
    {
        public static readonly DependencyProperty NowProgressProperty = DependencyProperty.Register(
            "NowProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

        public double NowProgress
        {
            get => (double)GetValue(NowProgressProperty);
            set => SetValue(NowProgressProperty, value);
        }

        public static readonly DependencyProperty TotalProgressProperty = DependencyProperty.Register(
            "TotalProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

        public double TotalProgress
        {
            get => (double)GetValue(TotalProgressProperty);
            set => SetValue(TotalProgressProperty, value);
        }

        public static readonly DependencyProperty AlbumCoverProperty = DependencyProperty.Register(
            "AlbumCover", typeof(Brush), typeof(CompactPlayerPage), new PropertyMetadata(default(Brush)));

        public Brush AlbumCover
        {
            get => (Brush)GetValue(AlbumCoverProperty);
            set => SetValue(AlbumCoverProperty, value);
        }

        public static readonly DependencyProperty ControlHoverProperty = DependencyProperty.Register(
            "ControlHover", typeof(Brush), typeof(CompactPlayerPage),
            new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

        public Brush ControlHover
        {
            get => (Brush)GetValue(ControlHoverProperty);
            set => SetValue(ControlHoverProperty, value);
        }


        public string LyricText
        {
            get { return (string)GetValue(LyricTextProperty); }
            set { SetValue(LyricTextProperty, value); }
        }

        public static readonly DependencyProperty LyricTextProperty =
            DependencyProperty.Register("LyricText", typeof(string), typeof(CompactPlayerPage),
                new PropertyMetadata("双击此处回正常窗口"));


        public string LyricTranslation
        {
            get { return (string)GetValue(LyricTranslationProperty); }
            set { SetValue(LyricTranslationProperty, value); }
        }

        public static readonly DependencyProperty LyricTranslationProperty =
            DependencyProperty.Register("LyricTranslation", typeof(string), typeof(CompactPlayerPage),
                new PropertyMetadata("右键可切换模糊保留"));


        public string NowPlayingName
        {
            get { return (string)GetValue(NowPlayingNameProperty); }
            set { SetValue(NowPlayingNameProperty, value); }
        }

        public static readonly DependencyProperty NowPlayingNameProperty =
            DependencyProperty.Register("NowPlayingName", typeof(string), typeof(CompactPlayerPage),
                new PropertyMetadata(string.Empty));


        public string NowPlayingArtists
        {
            get { return (string)GetValue(NowPlayingArtistsProperty); }
            set { SetValue(NowPlayingArtistsProperty, value); }
        }

        public static readonly DependencyProperty NowPlayingArtistsProperty =
            DependencyProperty.Register("NowPlayingArtists", typeof(string), typeof(CompactPlayerPage),
                new PropertyMetadata(string.Empty));


        bool forceBlur = true;

        public CompactPlayerPage()
        {
            this.InitializeComponent();
            HyPlayList.OnPlayPositionChange +=
                position => Common.Invoke(() => NowProgress = position.TotalMilliseconds);
            HyPlayList.OnPlayItemChange += OnChangePlayItem;
            HyPlayList.OnPlay += () => Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB4");
            HyPlayList.OnPause += () => Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB5");
            HyPlayList.OnLyricChange += OnLyricChanged;
            CompactPlayerAni.Begin();
        }

        private void OnLyricChanged()
        {
            if (HyPlayList.Lyrics.Count <= HyPlayList.LyricPos) return;
            Common.Invoke(() =>
            {
                LyricText = HyPlayList.Lyrics[HyPlayList.LyricPos].PureLyric;
                LyricTranslation = HyPlayList.Lyrics[HyPlayList.LyricPos].Translation;
            });
        }

        public async void OnChangePlayItem(HyPlayItem item)
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
            BitmapImage img = null;
            if (item != null)
                if (!Common.Setting.noImage)
                    if (item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                    {
                        img = new BitmapImage();
                        await img.SetSourceAsync(
                            await HyPlayList.NowPlayingStorageFile?.GetThumbnailAsync(ThumbnailMode.SingleItem,
                                9999));
                    }
                    else
                    {
                        img = new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover));
                    }

            Common.Invoke(() =>
            {
                TotalProgress = item?.PlayItem?.LengthInMilliseconds ?? 0;
                AlbumCover = new ImageBrush() { ImageSource = img, Stretch = Stretch.UniformToFill };
            });
        }

        private void MovePrevious(object sender, RoutedEventArgs e)
        {
            HyPlayList.SongMovePrevious();
        }

        private void MoveNext(object sender, RoutedEventArgs e)
        {
            HyPlayList.SongMoveNext();
        }

        private void ChangePlayState(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.IsPlaying) HyPlayList.Player.Pause();
            else HyPlayList.Player.Play();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            OnChangePlayItem(HyPlayList.NowPlayingItem);
            PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB4" : "\uEDB5";
            Common.BarPlayBar.Visibility = Visibility.Collapsed;
            Window.Current.SetTitleBar(SongNameContainer);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Common.BarPlayBar.Visibility = Visibility.Visible;
        }

        private void ExitCompactMode(object sender, DoubleTappedRoutedEventArgs e)
        {
            ApplicationView.GetForCurrentView().TryEnterViewModeAsync(ApplicationViewMode.Default);
            Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer));
        }

        private void CompactPlayerPage_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ControlHover = new Microsoft.Toolkit.Uwp.UI.Media.BackdropBlurBrush() { Amount = 10.0 };
            GridBtns.Visibility = Visibility.Visible;
        }

        private void CompactPlayerPage_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!forceBlur)
                ControlHover = new SolidColorBrush(Colors.Transparent);
            GridBtns.Visibility = Visibility.Collapsed;
        }

        private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            forceBlur = !forceBlur;
        }
    }
}