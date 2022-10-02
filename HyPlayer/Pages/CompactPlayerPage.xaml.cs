using System;
using System.IO;
using Windows.Storage.FileProperties;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Microsoft.Toolkit.Uwp.UI.Media;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class CompactPlayerPage : Page
{
    public static readonly DependencyProperty NowProgressProperty = DependencyProperty.Register(
        "NowProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

    public static readonly DependencyProperty TotalProgressProperty = DependencyProperty.Register(
        "TotalProgress", typeof(double), typeof(CompactPlayerPage), new PropertyMetadata(default(double)));

    public static readonly DependencyProperty AlbumCoverProperty = DependencyProperty.Register(
        "AlbumCover", typeof(Brush), typeof(CompactPlayerPage), new PropertyMetadata(default(Brush)));

    public static readonly DependencyProperty ControlHoverProperty = DependencyProperty.Register(
        "ControlHover", typeof(Brush), typeof(CompactPlayerPage),
        new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    public static readonly DependencyProperty LyricTextProperty =
        DependencyProperty.Register("LyricText", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata("双击此处回正常窗口"));

    public static readonly DependencyProperty LyricTranslationProperty =
        DependencyProperty.Register("LyricTranslation", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata("右键可切换模糊保留"));

    public static readonly DependencyProperty NowPlayingNameProperty =
        DependencyProperty.Register("NowPlayingName", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NowPlayingArtistsProperty =
        DependencyProperty.Register("NowPlayingArtists", typeof(string), typeof(CompactPlayerPage),
            new PropertyMetadata(string.Empty));


    private bool forceBlur = true;

    public CompactPlayerPage()
    {
        InitializeComponent();
        HyPlayList.OnPlayPositionChange +=
            position => Common.Invoke(() => NowProgress = position.TotalMilliseconds);
        HyPlayList.OnPlayItemChange += OnChangePlayItem;
        HyPlayList.OnPlay += () => Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB4");
        HyPlayList.OnPause += () => Common.Invoke(() => PlayStateIcon.Glyph = "\uEDB5");
        HyPlayList.OnLyricChange += OnLyricChanged;
        CompactPlayerAni.Begin();
    }

    public double NowProgress
    {
        get => (double)GetValue(NowProgressProperty);
        set => SetValue(NowProgressProperty, value);
    }

    public double TotalProgress
    {
        get => (double)GetValue(TotalProgressProperty);
        set => SetValue(TotalProgressProperty, value);
    }

    public Brush AlbumCover
    {
        get => (Brush)GetValue(AlbumCoverProperty);
        set => SetValue(AlbumCoverProperty, value);
    }

    public Brush ControlHover
    {
        get => (Brush)GetValue(ControlHoverProperty);
        set => SetValue(ControlHoverProperty, value);
    }


    public string LyricText
    {
        get => (string)GetValue(LyricTextProperty);
        set => SetValue(LyricTextProperty, value);
    }


    public string LyricTranslation
    {
        get => (string)GetValue(LyricTranslationProperty);
        set => SetValue(LyricTranslationProperty, value);
    }


    public string NowPlayingName
    {
        get => (string)GetValue(NowPlayingNameProperty);
        set => SetValue(NowPlayingNameProperty, value);
    }


    public string NowPlayingArtists
    {
        get => (string)GetValue(NowPlayingArtistsProperty);
        set => SetValue(NowPlayingArtistsProperty, value);
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

    private void OnChangePlayItem(HyPlayItem item)
    {
        Common.Invoke(async () =>
        {
            NowPlayingName = item?.PlayItem?.Name;
            NowPlayingArtists = item?.PlayItem?.ArtistString;
            BitmapImage img = null;
            if (item != null)
                if (!Common.Setting.noImage)
                    if (item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                    {
                        img = new BitmapImage();
                        if (!Common.Setting.useTaglibPicture || item.PlayItem?.LocalFileTag is null || item.PlayItem.LocalFileTag.Pictures.Length == 0)
                        {
                            await img.SetSourceAsync(
                                await HyPlayList.NowPlayingStorageFile?.GetThumbnailAsync(ThumbnailMode.MusicView, 9999));
                        }
                        else
                        {
                            await img.SetSourceAsync(new MemoryStream(item.PlayItem.LocalFileTag.Pictures[0].Data.Data).AsRandomAccessStream());
                        }
                    }
                    else
                    {
                        img = new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover));
                    }

            TotalProgress = item?.PlayItem?.LengthInMilliseconds ?? 0;
            AlbumCover = new ImageBrush { ImageSource = img, Stretch = Stretch.UniformToFill };
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
        ControlHover = new BackdropBlurBrush { Amount = 10.0 };
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