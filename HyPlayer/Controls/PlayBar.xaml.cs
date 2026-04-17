#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.System.Profile;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using WinRT;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.Controls;

public sealed partial class PlayBar
{
    // ---------------------------------------------------------------
    //  ViewModel (resolved from DI; holds all business logic)
    // ---------------------------------------------------------------
    public PlayBarViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<PlayBarViewModel>();

    // ---------------------------------------------------------------
    //  UI-only fields (kept in code-behind)
    // ---------------------------------------------------------------
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();

    private SolidColorBrush BackgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding = false;
    public PlayMode NowPlayType => ViewModel.NowPlayType;
    private TimeSpan StartingTimeSpan = TimeSpan.Zero;
    public ObservableCollection<HyPlayItem> PlayItems => ViewModel.PlaylistItems;
#nullable enable
    private ManipulationStartedRoutedEventArgs? _slidingEventArgs = null;
#nullable restore
    private bool realSelectSong;

    /*
private Storyboard TbSongNameScrollStoryBoard;
private double lastOffsetX;
DoubleAnimation verticalAnimation;
*/

    public PlayBar()
    {
        Common.BarPlayBar = this;
        InitializeComponent();
        _player.OnGlobalPlaybackStatusChanged += Player_OnGlobalPlaybackStatusChanged;
    }

    private void Player_OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
                        _player.GlobalPlaybackStatus == PlaybackStatus.Playing
                            ? "\uF8AE"
                            : "\uF5B0";
            if (status == PlaybackStatus.Playing)
            {
                if (Common.Setting.playbarBackgroundBreath)
                    PlayBarBackgroundAni.Begin();
            }
            else
            {
                PlayBarBackgroundAni.Stop();
            }
        });
    }

    private void HyPlayListOnOnSongRemoveAll()
    {
        _ = Common.Invoke(() =>
        {
            PlayItems.Clear();
            PlayListTitle.Text = "播放列表";
        });
    }

    public void OnPlayPositionChange(TimeSpan ts)
    {
        _ = Common.Invoke(() =>
        {
            try
            {
                if (ViewModel.NowPlayingItem?.PlayItem == null) return;
                var _lyricIsOnShowTimespan = ts;
                if (!_isSliding)
                {
                    SliderProgress.Value = _player.PrimaryAudioInputNode?.Position.TotalMilliseconds ?? 0;
                }

                if ((_player.PrimaryAudioInputNode?.Position.Hours ?? 0) == 0)
                {
                    if ((_player.PrimaryAudioInputNode?.Position.Minutes ?? 0) < 10)
                        TextBlockNowTime.Text =
                            _player.PrimaryAudioInputNode?.Position.ToString(@"m\:ss") ?? string.Empty;
                    else
                        TextBlockNowTime.Text =
                            _player.PrimaryAudioInputNode?.Position.ToString(@"mm\:ss") ?? string.Empty;
                }
                else
                {
                    TextBlockNowTime.Text =
                        _player.PrimaryAudioInputNode?.Position.ToString(@"hh\:mm\:ss") ?? string.Empty;
                }
            }
            catch
            {
                //ignore
            }
        });
    }

    public void SetPlayBarIdleBackground(SolidColorBrush colorBrush)
    {
        var color = colorBrush.Color;
        color.A = 80;
        BackgroundElayBrush = new SolidColorBrush(color);
    }

    public void LoadPlayingFile(HyPlayItem mpi)
    {
        if (ViewModel.NowPlayingItem == null) return;
        _ = Common.Invoke(() => ApplicationView.GetForCurrentView().Title =
                $"{ViewModel.NowPlayingItem.Name} - {ViewModel.NowPlayingItem.ArtistString}");

        //SliderAudioRate.Value = ViewModel.Volume * 100;

        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
            _player.GlobalPlaybackStatus == PlaybackStatus.Playing
                ? "\uF8AE"
                : "\uF5B0";
            if (Common.IsInFm)
            {
                IconPrevious.Glyph = "\uE7E8";
                IconPlayType.Glyph = "\uE107";
                FlyoutPlayRollType.Text = "我不喜欢";
            }
            else
            {
                IconPrevious.Glyph = "\uF8AC";
                var nowPlayType = ViewModel.NowPlayType;
                switch (nowPlayType)
                {
                    case PlayMode.Shuffled:
                        //随机
                        IconPlayType.Glyph = "\uE14B";
                        FlyoutPlayRollType.Text = "随机播放";
                        break;
                    case PlayMode.SinglePlay:
                        //单曲
                        IconPlayType.Glyph = "\uE1CC";
                        FlyoutPlayRollType.Text = "单曲循环";
                        break;
                    case PlayMode.DefaultRoll:
                        //顺序
                        IconPlayType.Glyph = "\uE169";
                        FlyoutPlayRollType.Text = "顺序播放";
                        break;
                }
            }

            // 恢复播放音量
            if (ViewModel.NowPlayingItem == null)
            {
                TbSingerName.Content = null;
                TbSongName.Text = null;
                TbAlbumName.Content = null;
                ApplicationView.GetForCurrentView().Title = "";
                TbSongTag.Text = "无歌曲";
                return;
            }

            var totalTime = TimeSpan.FromMilliseconds(ViewModel.NowPlayingItem.LengthInMilliseconds);
            if (totalTime.Hours == 0)
            {
                if (totalTime.Minutes < 10)
                    TextBlockTotalTime.Text = totalTime.ToString(@"m\:ss");
                else
                    TextBlockTotalTime.Text = totalTime.ToString(@"mm\:ss");
            }
            else
            {
                TextBlockTotalTime.Text = totalTime.ToString(@"hh\:mm\:ss");
            }


            if (ViewModel.NowPlayingItem?.PlayItem == null) return;

            if (_isSliding)
            {
                _slidingEventArgs?.Complete();
                _isSliding = false;
            }

            SliderProgress.Minimum = 0;
            SliderProgress.Maximum = ViewModel.NowPlayingItem.LengthInMilliseconds;
            SliderProgress.Value = _player.PrimaryAudioInputNode?.Position.TotalMilliseconds ?? 0;

            TextBlockNowTime.Text =
                _player.PrimaryAudioInputNode?.Position.ToString(@"m\:ss") ?? "0:00";

            TbSingerName.Content = ViewModel.NowPlayingItem.ArtistString;
            TbSongName.Text = ViewModel.NowPlayingItem.Name;
            TbAlbumName.Content = ViewModel.NowPlayingItem.AlbumString;

            // 新版随机播放算法
            realSelectSong = false;
            if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
                ListBoxPlayList.SelectedIndex = HyPlayList.ShufflingIndex;
            else
                ListBoxPlayList.SelectedIndex = ViewModel.NowPlayingIndex;

            realSelectSong = true;

            TbSongTag.Text = ViewModel.NowPlayingItem.QualityTag ?? "";
            Btn_Share.IsEnabled =
                mpi.ItemType != HyPlayItemType.Local && mpi.ItemType != HyPlayItemType.LocalProgressive;
        });
        var isLiked = Common.LikedSongs.Contains(mpi.Id);
        if (mpi.ItemType != HyPlayItemType.Local && mpi.ItemType != HyPlayItemType.LocalProgressive)
        {
            _ = Common.Invoke(() =>
            {
                IconLiked.Visibility = isLiked
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                FlyoutLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
                FlyoutLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
                //BtnFlyoutLike.IsChecked = Common.LikedSongs.Contains(ViewModel.NowPlayingItem.Id);
            });
            HistoryManagement.AddNCSongHistory(mpi.Id);
        }

        /*
        verticalAnimation.To = TbSongName.ActualWidth - TbSongName.Tb.ActualWidth;
        verticalAnimation.SpeedRatio = 0.1;
        TbSongNameScrollStoryBoard.Stop();
        TbSongNameScrollStoryBoard.Children.Clear();
        TbSongNameScrollStoryBoard.Children.Add(verticalAnimation);
        TbSongNameScrollStoryBoard.Begin();
        */
    }

    public void RefreshSongList(bool isShuffle = false)
    {
        ViewModel.RefreshPlaylistItems(isShuffle);
        PlayListTitle.Text = ViewModel.GetPlaylistTitle();

        var targetingIndex = ViewModel.GetTargetingIndex();
        if (targetingIndex == -1 || targetingIndex >= PlayItems.Count) return;
        realSelectSong = false;
        ListBoxPlayList.SelectedIndex = targetingIndex;
        realSelectSong = true;
    }

    private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_player.PlayerCreated || ViewModel.NowPlayingItem == null) return;
        ViewModel.TogglePlayPauseCommand.Execute(null);
    }

    private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SetVolumeCommand.Execute(e.NewValue);
    }

    private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
    {
        _player.IsMuted = !_player.IsMuted;
        BtnMuteIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        FlyoutBtnMuteIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        BtnVolIcon.Glyph = _player.IsMuted ? "\uE198" : "\uE15D";
        //SliderAudioRate.Visibility = _player.IsMuted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MovePreviousCommand.Execute(null);
    }

    private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveNextCommand.Execute(null);
    }

    private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxPlayList.SelectedItem != null && ListBoxPlayList.SelectedItem != ViewModel.NowPlayingItem &&
            realSelectSong)
        {
            ViewModel.MoveToItemCommand.Execute(ListBoxPlayList.SelectedItem as HyPlayItem);
            if (ViewModel.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
                HyPlayList.ShufflingIndex = ListBoxPlayList.SelectedIndex;
        }
    }

    public void ShowExpandedPlayer()
    {
        if (!_player.PlayerCreated || ViewModel.NowPlayingItem?.PlayItem?.AudioGraphPlaybackSource == null) return;
        ButtonExpand.Visibility = Visibility.Collapsed;
        ButtonCollapse.Visibility = Visibility.Visible;
        PlayBarBackgroundFadeOut.Begin();
        //Common.PageMain.MainFrame.Visibility = Visibility.Collapsed;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer), null,
            new EntranceNavigationTransitionInfo());
        Common.PageMain.GridPlayBar.BorderThickness = new Thickness(0);
        Common.PageMain.MainFrame.Visibility = Visibility.Collapsed;
        Common.PageMain.GridPlayBarMarginBlur.Visibility = Visibility.Collapsed;
        if (Common.Setting.expandAnimation && GridSongInfoContainer.Visibility == Visibility.Visible)
            try
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TbSongName);
                if (GridSongInfoContainer.Visibility == Visibility.Visible)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", AlbumImage);

                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TbSingerName);
                Common.PageExpandedPlayer.StartExpandAnimation();
            }
            catch
            {
                //ignore
            }

        if (Common.Setting.forceMemoryGarbage)
            Common.NavigatePage(typeof(BlankPage));
        Common.IsExpanded = true;
        GridSongInfo.Visibility = Visibility.Collapsed;
        GridSongAdvancedOperation.Visibility = Visibility.Visible;
    }

    private void ButtonExpand_OnClick(object sender, RoutedEventArgs e)
    {
        ShowExpandedPlayer();
    }

    public void ButtonCollapse_OnClick(object sender, RoutedEventArgs e)
    {
        CollapseExpandedPlayer();
    }

    public void CollapseExpandedPlayer()
    {
        Common.PageMain.IsExpandedPlayerInitialized = false;
        if (Common.PageExpandedPlayer == null) return;
        Common.PageExpandedPlayer.StartCollapseAnimation();
        GridSongAdvancedOperation.Visibility = Visibility.Collapsed;
        GridSongInfo.Visibility = Visibility.Visible;
        PlayBarBackgroundFadeIn.Begin();
        Common.BrushManagement.AccentBrush = null;
        if (Common.Setting.expandAnimation && GridSongInfoContainer.Visibility == Visibility.Visible)
        {
            ConnectedAnimation anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
            ConnectedAnimation anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
            ConnectedAnimation anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
            ConnectedAnimation anim4 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongAlbum");
            anim4?.Configuration = new DirectConnectedAnimationConfiguration();
            anim3?.Configuration = new DirectConnectedAnimationConfiguration();
            anim2?.Configuration = new DirectConnectedAnimationConfiguration();
            anim1?.Configuration = new DirectConnectedAnimationConfiguration();
            try
            {
                anim3?.TryStart(TbSingerName);
                anim1?.TryStart(TbSongName);
                anim2?.TryStart(AlbumImage);
                anim4?.TryStart(TbAlbumName);
            }
            catch
            {
                //ignore
            }
        }

        if (Common.Setting.forceMemoryGarbage)
            Common.NavigateBack();
        ButtonExpand.Visibility = Visibility.Visible;
        ButtonCollapse.Visibility = Visibility.Collapsed;
        Common.PageMain.GridPlayBarMarginBlur.Visibility = Visibility.Visible;
        Common.PageBase.AppTitleBar.ReleasePointerCaptures();
        Common.PageExpandedPlayer = null;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
        Common.PageMain.GridPlayBar.BorderThickness = new Thickness(1);
        Common.PageMain.MainFrame.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Collapsed;
        var region = Common.PageBase.AppTitleBar.FindDescendant("PART_DragRegion")?.As<Grid>();
        Window.Current.SetTitleBar(region);
        Common.IsExpanded = false;
        RefreshPlayBarCover(ViewModel.NowPlayingItem);
    }

    private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RemoveAllCommand.Execute(null);
    }

    private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
    {
        // TODO: Migrate PickLocalFile to IPlaylistService.AppendStorageFilesAsync
        _ = HyPlayList.PickLocalFile();
    }

    private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var item = btn.DataContext as HyPlayItem;
            ViewModel.RemoveItemCommand.Execute(item);
            RefreshSongList();
        }
    }

    private void BtnPlayRollType_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Common.IsInFm)
        {
            ViewModel.ChangePlayModeCommand.Execute(null);
            // Update UI icons based on new play mode
            switch (ViewModel.NowPlayType)
            {
                case PlayMode.Shuffled:
                    IconPlayType.Glyph = "\uE14B";
                    FlyoutPlayRollType.Text = "随机播放";
                    RefreshSongList();
                    break;
                case PlayMode.SinglePlay:
                    IconPlayType.Glyph = "\uE1CC";
                    FlyoutPlayRollType.Text = "单曲循环";
                    break;
                case PlayMode.DefaultRoll:
                    IconPlayType.Glyph = "\uE169";
                    FlyoutPlayRollType.Text = "顺序播放";
                    RefreshSongList();
                    break;
            }
        }
        else
        {
            _ = Common.NeteaseAPI.RequestAsync(NeteaseApis.PersonalFmTrashApi,
                new FmTrashRequest
                {
                    Id = ViewModel.NowPlayingItem.Id
                });
            PersonalFM.LoadNextFMStatic();
        }
    }

    private void BtnLike_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.LikeSong();
    }

    private async void TbSingerName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (ViewModel.NowPlayingItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    Common.NavigatePage(typeof(Me), ViewModel.NowPlayingItem.Artist[0].Id);
                }
                else
                {
                    if (ViewModel.NowPlayingItem.Artist.Count > 1)
                        await new ArtistSelectDialog(ViewModel.NowPlayingItem.Artist).ShowAsync();
                    else
                        Common.NavigatePage(typeof(ArtistPage),
                            ViewModel.NowPlayingItem.Artist[0].Id);
                }

                //CollapseExpandedPlayer();
            }
        }
        catch
        {
        }
    }

    private void TbAlbumName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (ViewModel.NowPlayingItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    Common.NavigatePage(typeof(Me), ViewModel.NowPlayingItem.Artist[0].Id);
                }
                else
                {
                    if (ViewModel.NowPlayingItem.Album.Id != "0")
                        Common.NavigatePage(typeof(AlbumPage),
                            ViewModel.NowPlayingItem.Album.Id);
                }
            }
        }
        catch
        {
        }
    }

    private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            await new SongListSelect(ViewModel.NowPlayingItem.Id).ShowAsync();
    }

    private void Btn_Down_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType is HyPlayItemType.Netease or HyPlayItemType.Radio)
        {
            DownloadManager.AddDownload(ViewModel.NowPlayingItem.ToNCSong());
        }
    }

    private void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            Common.NavigatePage(typeof(Comments), "sg" + ViewModel.NowPlayingItem.Id);
        else
            Common.NavigatePage(typeof(Comments), "fm" + ViewModel.NowPlayingItem.Album.Alias);
        if (Common.Setting.forceMemoryGarbage)
            Common.NavigatePage(typeof(BlankPage));
        CollapseExpandedPlayer();
    }

    private void Btn_Share_OnClick(object sender, RoutedEventArgs e)
    {
        //TODO: 分享电台节目
        if (ViewModel.NowPlayingItem.ItemType != HyPlayItemType.Netease) return;
        var dataTransferManager = DataTransferManager.GetForCurrentView();

        dataTransferManager.DataRequested += (manager, args) =>
        {
            var dataPackage = new DataPackage();
            dataPackage.SetWebLink(new Uri("https://music.163.com/#/song?Id=" +
                                           ViewModel.NowPlayingItem.Id));
            dataPackage.Properties.Title = ViewModel.NowPlayingItem.Name;
            dataPackage.Properties.Description =
                "歌手: " + string.Join(';',
                    ViewModel.NowPlayingItem.Artist
                        .Select(t => t.Name));
            var request = args.Request;
            request.Data = dataPackage;
        };

        //展示系统的共享ui
        DataTransferManager.ShowShareUI();
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // 当前未打开歌词
        Bindings.Update();
        var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
        if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                "306200B4771A6.217957860C1A5_mb3g82vhcggpy") != LaunchQuerySupportStatus.Available)
        {
            var dlg = new ContentDialog
            {
                Title = "关于桌面歌词",
                Content =
                    "目前 HyPlayer 已经适配「热词」，我们推荐使用「热词」来获得真正的桌面歌词体验，可以前往 Microsoft 商店安装 「热词」",
                CloseButtonText = "否",
                PrimaryButtonText = "安装 「热词」"
            };

            var res = await dlg.ShowAsync(ContentDialogPlacement.Popup);
            if (res == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9"));
                return;
            }

            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri, new LauncherOptions
            {
                FallbackUri = new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9")
            });
            Bindings.Update();
        }
        catch
        {

        }
    }

    private void BtnPlayStateChange_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        //BtnPlayStateChange_OnClick(sender, e);
    }

    private void ImageContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ButtonExpand_OnClick(sender, e);
    }

    private void ButtonPlayList_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.NowPlayingIndex >= 0 && ViewModel.NowPlayingIndex < PlayItems.Count)
        {
            var nowPlayType = ViewModel.NowPlayType;
            if (nowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
                // 新的随机算法
                ListBoxPlayList.ScrollIntoView(PlayItems[HyPlayList.ShufflingIndex]);
            else
                ListBoxPlayList.ScrollIntoView(PlayItems[ViewModel.NowPlayingIndex]);
        }
    }

    private void ImageContainer_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AlbumImageHover.Visibility = Visibility.Visible;
    }

    private void ImageContainer_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AlbumImageHover.Visibility = Visibility.Collapsed;
    }

    private void FlyoutBtnVolume_OnClick(object sender, RoutedEventArgs e)
    {
        FlyoutBtnVolume.ContextFlyout?.ShowAt(BtnMore);
    }

    private void FlyoutBtnPlayList_OnClick(object sender, RoutedEventArgs e)
    {
        FlyoutBtnPlayList.ContextFlyout?.ShowAt(BtnMore);
        ButtonPlayList_OnClick(sender, e);
    }

    private void OnEnteringForeground()
    {
        LoadPlayingFile(ViewModel.NowPlayingItem);
        RefreshPlayBarCover(ViewModel.NowPlayingItem);
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializedAni.Begin();
        PlayBarBackgroundFadeIn.Begin();
        ViewModel.SetVolumeCommand.Execute((double)Common.Setting.Volume);
        SliderAudioRate.Value = (double)Common.Setting.Volume;

        // --- Messenger-based event subscriptions (replacing HyPlayList.OnXxx) ---
        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<TrackChangedMessage>(this, (_, m) => LoadPlayingFile(m.Item));
        messenger.Register<PlaylistChangedMessage>(this, (_, m) =>
        {
            if (!m.IsShuffleTrigger && ViewModel.Items.Count == 0)
                HyPlayListOnOnSongRemoveAll();
            else
                RefreshSongList(m.IsShuffleTrigger);
        });
        messenger.Register<SongLikeStatusChangedMessage>(this, (_, m) => HyPlayList_OnSongLikeStatusChange(m.IsLiked));
        messenger.Register<CoverChangedMessage>(this, (_, m) => RefreshPlayBarCover(m.Item));
        messenger.Register<LoginCompletedMessage>(this, (_, _) => HyPlayListOnOnLoginDone());

        // Position updates now use Messenger too
        messenger.Register<PositionTickMessage>(this, (_, m) => OnPlayPositionChange(m.Position));

        Common.OnEnterForegroundFromBackground += OnEnteringForeground;
        if (Common.Setting.playbarButtonsTransparent)
        {
            BtnPlayRollType.Background = new SolidColorBrush(Colors.Transparent);
            BtnPreviousSong.Background = new SolidColorBrush(Colors.Transparent);
            BtnPlayStateChange.Background = new SolidColorBrush(Colors.Transparent);
            BtnNextSong.Background = new SolidColorBrush(Colors.Transparent);
            BtnLike.Background = new SolidColorBrush(Colors.Transparent);
        }

        if (Common.Setting.playButtonAccentColor)
        {
            BtnPlayStateChange.Background = Resources["SolidPlayButtonColor"]?.As<Brush>();
            PlayStateIcon.Foreground = Resources["SolidPlayButtonIconColor"]?.As<Brush>();
        }
        else
            PlayBarBackgroundAni.Children.RemoveAt(2);

        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
        realSelectSong = false;
        realSelectSong = true;
        Common.Logs.Add("Now PlaySource is " + ViewModel.PlaySourceId);

        if (Common.IsExpanded)
            Common.BarPlayBar.ShowExpandedPlayer();
        if (!Common.Setting.playbarBackgroundAcrylic)
            if (Common.Setting.hotlyricOnStartup)
                try
                {
                    var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
                    if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri,
                            "306200B4771A6.217957860C1A5_mb3g82vhcggpy") ==
                        LaunchQuerySupportStatus.Available)
                    {
                        await Launcher.LaunchUriAsync(uri);
                        Bindings.Update();
                        return;
                    }
                }
                catch
                {
                }

        if (Common.Setting.playbarBackgroundElay)
        {
            PointerEntered += (o, args) =>
            {
                if (Common.IsExpanded && Common.Setting.playbarBackgroundElay)
                    GridThis.Background = BackgroundElayBrush;
            };
            PointerExited += (o, args) => { GridThis.Background = new SolidColorBrush(Colors.Transparent); };
        }

    }

    public async void RefreshPlayBarCover(HyPlayItem playItem)
    {
        if (ViewModel.CoverStream == null) return;
        _ = Common.Invoke(async () =>
        {
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
            {
                try
                {
                    if (playItem != ViewModel.NowPlayingItem) return;
                    using var stream = ViewModel.CoverStream.CloneStream();
                    await AlbumImageSource.SetSourceAsync(stream);
                }
                catch
                {
                    //Ignore
                }
            }
        });
    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        IconLiked.Visibility = isLiked
            ? Visibility.Visible
            : Visibility.Collapsed;
        FlyoutLiked.Foreground = isLiked
            ? new SolidColorBrush(Colors.Red)
            : Application.Current.Resources["TextFillColorPrimaryBrush"]?.As<Brush>();
        FlyoutLiked.Glyph = isLiked
            ? "\uE00B"
            : "\uE006";
    }

    private async void HyPlayListOnOnLoginDone()
    {
        if (ViewModel.PlaySourceId == "local") return;
        try
        {
            var list = await HistoryManagement.GetcurPlayingListHistory();
            if (list.Count > 0)
            {
                var success = int.TryParse(ApplicationData.Current.LocalSettings.Values["nowSongPointer"]?.ToString(),
                    out var result);
                if (success)
                {
                    // TODO: Migrate AppendNcSongs + NotifyPlayItemChanged to IPlaylistService
                    HyPlayList.NowPlaying = result;
                    HyPlayList.AppendNcSongs(list);
                    HyPlayList.NotifyPlayItemChanged(HyPlayList.NowPlayingItem);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABStartPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABEndPoint = _player.PrimaryAudioInputNode.Position;
    }

    private void ABRepeatStateButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABRepeatStatus = !Common.Setting.ABRepeatStatus;
    }

    private void SliderProgress_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _slidingEventArgs = null;
        var value = TimeSpan.FromMilliseconds(SliderProgress.Value);
        if (Math.Abs((value - StartingTimeSpan).TotalMilliseconds) > 250d)
        {
            ViewModel.SeekCommand.Execute(value);
        }

        _isSliding = false;
    }

    private void SliderProgress_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _isSliding = true;
        _slidingEventArgs = e;
    }

    private void SliderProgress_OnManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
    {
        var value = TimeSpan.FromMilliseconds(SliderProgress.Value);
        StartingTimeSpan = value;
        ViewModel.SeekCommand.Execute(value);
    }

    private void CopySongDetailFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        switch ((sender?.As<MenuFlyoutItem>()).Name)
        {
            case "CopySongNameFlyoutItem":
                if (TbSongName.Text == null) return;
                package.SetText(TbSongName.Text);
                break;
            case "CopySingerNameFlyoutItem":
                if (TbSingerName.Content == null) return;
                package.SetText(TbSingerName.Content.ToString());
                break;
            case "CopyAlbumNameFlyoutItem":
                if (TbAlbumName.Content == null) return;
                package.SetText(TbAlbumName.Content.ToString());
                break;
        }

        package.RequestedOperation = DataPackageOperation.Copy;
        Clipboard.SetContent(package);
    }

    private void BtnReverse_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Migrate List.Reverse to IPlaylistService (needs mutable list API or dedicated method)
        HyPlayList.List.Reverse();
        ViewModel.NotifyAppendDone();
        HyPlayList.NowPlaying = HyPlayList.List.Count - HyPlayList.NowPlaying - 1;
    }
}