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

        public CompactPlayerPage()
        {
            this.InitializeComponent();
            HyPlayList.OnPlayPositionChange += position => NowProgress = position.TotalMilliseconds;
            HyPlayList.OnPlayItemChange += OnChangePlayItem;
            HyPlayList.OnPlay += () => PlayStateIcon.Glyph = "\uEDB4";
            HyPlayList.OnPause += () => PlayStateIcon.Glyph = "\uEDB5";
        }

        public async void OnChangePlayItem(HyPlayItem item)
        {
            BitmapImage img = null;
            if (!Common.Setting.noImage)
                if (item.ItemType == HyPlayItemType.Local)
                {
                    img = new BitmapImage();
                    await img.SetSourceAsync(
                        await HyPlayList.NowPlayingStorageFile?.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999));
                }
                else
                {
                    img = new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover));
                }

            TotalProgress = item.PlayItem.LengthInMilliseconds;
            AlbumCover = new ImageBrush() { ImageSource = img };
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
            if (HyPlayList.isPlaying) HyPlayList.Player.Pause();
            else HyPlayList.Player.Play();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            OnChangePlayItem(HyPlayList.NowPlayingItem);
            PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB4" : "\uEDB5";
            Common.BarPlayBar.Visibility = Visibility.Collapsed;
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
            ControlHover = new SolidColorBrush(Colors.Transparent);
            GridBtns.Visibility = Visibility.Collapsed;
        }
    }
}