#region

using CommunityToolkit.WinUI;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;
using HyPlayer.Pages;
using HyPlayer.UWP.Chopin.Abstractions.Models;
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

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlayBar
{
    private SolidColorBrush BackgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding = false;
    public PlayMode NowPlayType = PlayMode.DefaultRoll;
    private TimeSpan StartingTimeSpan = TimeSpan.Zero;
    public ObservableCollection<HyPlayItem> PlayItems = new();
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
        HyPlayList.Player.OnGlobalPlaybackStatusChanged += Player_OnGlobalPlaybackStatusChanged;
    }

    private void Player_OnGlobalPlaybackStatusChanged(PlaybackStatus status)
    {
        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
                        HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
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
                if (HyPlayList.NowPlayingItem?.PlayItem == null) return;
                var _lyricIsOnShowTimespan = ts;
                if (!_isSliding)
                {
                    SliderProgress.Value = HyPlayList.Player.PrimaryAudioInputNode?.Position.TotalMilliseconds ?? 0;
                }

                if ((HyPlayList.Player.PrimaryAudioInputNode?.Position.Hours ?? 0) == 0)
                {
                    if ((HyPlayList.Player.PrimaryAudioInputNode?.Position.Minutes ?? 0) < 10)
                        TextBlockNowTime.Text =
                            HyPlayList.Player.PrimaryAudioInputNode?.Position.ToString(@"m\:ss") ?? string.Empty;
                    else
                        TextBlockNowTime.Text =
                            HyPlayList.Player.PrimaryAudioInputNode?.Position.ToString(@"mm\:ss") ?? string.Empty;
                }
                else
                {
                    TextBlockNowTime.Text =
                        HyPlayList.Player.PrimaryAudioInputNode?.Position.ToString(@"hh\:mm\:ss") ?? string.Empty;
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
        if (HyPlayList.NowPlayingItem.PlayItem == null) return;
        try
        {
            _ = Common.Invoke(() => ApplicationView.GetForCurrentView().Title =
                $"{HyPlayList.NowPlayingItem.PlayItem.Name} - {HyPlayList.NowPlayingItem.PlayItem.ArtistString}");
        }
        catch (Exception)
        {
            //IGNORE
        }

        //SliderAudioRate.Value = HyPlayList.Player.Volume * 100;

        _ = Common.Invoke(() =>
        {
            PlayStateIcon.Glyph =
            HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Playing
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
                switch (HyPlayList.NowPlayType)
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
            if (HyPlayList.NowPlayingItem.PlayItem == null)
            {
                TbSingerName.Content = null;
                TbSongName.Text = null;
                TbAlbumName.Content = null;
                ApplicationView.GetForCurrentView().Title = "";
                TbSongTag.Text = "无歌曲";
                return;
            }

            var totalTime = TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds);
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


            if (HyPlayList.NowPlayingItem?.PlayItem == null) return;

            if (_isSliding)
            {
                _slidingEventArgs?.Complete();
                _isSliding = false;
            }

            SliderProgress.Minimum = 0;
            SliderProgress.Maximum = HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds;
            SliderProgress.Value = HyPlayList.Player.PrimaryAudioInputNode?.Position.TotalMilliseconds ?? 0;

            TextBlockNowTime.Text =
                HyPlayList.Player.PrimaryAudioInputNode?.Position.ToString(@"m\:ss") ?? "0:00";

            TbSingerName.Content = HyPlayList.NowPlayingItem.PlayItem.ArtistString;
            TbSongName.Text = HyPlayList.NowPlayingItem.PlayItem.Name;
            TbAlbumName.Content = HyPlayList.NowPlayingItem.PlayItem.AlbumString;

            // 新版随机播放算法
            realSelectSong = false;
            if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
                ListBoxPlayList.SelectedIndex = HyPlayList.ShufflingIndex;
            else
                ListBoxPlayList.SelectedIndex = HyPlayList.NowPlaying;

            realSelectSong = true;

            TbSongTag.Text = HyPlayList.NowPlayingItem.PlayItem.QualityTag ?? "";
            Btn_Share.IsEnabled =
                HyPlayList.NowPlayingItem?.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive;
        });
        var isLiked = Common.LikedSongs.Contains(mpi.PlayItem.Id);
        if (mpi.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
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
                //BtnFlyoutLike.IsChecked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.Id);
            });
            HistoryManagement.AddNCSongHistory(mpi.PlayItem.Id);
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
        try
        {
            List<HyPlayItem> targetingList;
            int targetingIndex;
            // 新版随机播放算法
            if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
            {
                targetingIndex = HyPlayList.ShufflingIndex;
                targetingList = HyPlayList.ShuffleList.Select(t => HyPlayList.List[t]).ToList();
                PlayListTitle.Text = "随机播放列表 (共" + targetingList.Count + "首)";
            }
            else
            {
                targetingIndex = HyPlayList.NowPlaying;
                targetingList = HyPlayList.List;
                PlayListTitle.Text = "播放列表 (共" + targetingList.Count + "首)";
            }

            /*
            var vpos = -1;
            for (var b = 0; b < PlayItems.Count; b++)
                if (!targetingList.Contains(PlayItems[b]))
                    PlayItems.RemoveAt(b);

            foreach (var t in targetingList)
            {
                vpos++;
                if (!PlayItems.Contains(t)) PlayItems.Insert(vpos, t);
            }
            */

            realSelectSong = false;
            PlayItems.Clear();
            targetingList.ForEach(PlayItems.Add);
            realSelectSong = true;

            if (targetingIndex == -1 || targetingIndex >= PlayItems.Count) return;
            realSelectSong = false;
            ListBoxPlayList.SelectedIndex = targetingIndex;
            realSelectSong = true;
        }
        catch
        {
        }
    }

    private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (!HyPlayList.Player.PlayerCreated || HyPlayList.NowPlayingItem.PlayItem == null) return;
        if (HyPlayList.NowPlayingItem.PlayItem?.Name != null && HyPlayList.Player.GlobalPlaybackStatus == PlaybackStatus.Closed)
            _ = HyPlayList.LoadMediaSource(HyPlayList.List[HyPlayList.NowPlaying]);
        if (HyPlayList.IsPlaying)
        {
            HyPlayList.Player.PauseAll();
        }
        else
        {
            HyPlayList.Player.PlayAll();
        }
    }

    private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        HyPlayList.PlayerOutgoingVolume = e.NewValue / 100;
    }

    private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
    {
        HyPlayList.Player.IsMuted = !HyPlayList.Player.IsMuted;
        BtnMuteIcon.Glyph = HyPlayList.Player.IsMuted ? "\uE198" : "\uE15D";
        FlyoutBtnMuteIcon.Glyph = HyPlayList.Player.IsMuted ? "\uE198" : "\uE15D";
        BtnVolIcon.Glyph = HyPlayList.Player.IsMuted ? "\uE198" : "\uE15D";
        //SliderAudioRate.Visibility = HyPlayList.Player.IsMuted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
    {
        if (Common.IsInFm)
            PersonalFM.ExitFm();
        else
            HyPlayList.SongMovePrevious();
    }

    private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.SongMoveNext();
    }

    private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
            Common.Setting.displayShuffledList)
        {
            if (ListBoxPlayList.SelectedItem != null && ListBoxPlayList.SelectedItem != HyPlayList.NowPlayingItem &&
                realSelectSong)
            {
                HyPlayList.SongMoveTo(ListBoxPlayList.SelectedItem as HyPlayItem);
                HyPlayList.ShufflingIndex = ListBoxPlayList.SelectedIndex;
            }
        }
        else
        {
            if (ListBoxPlayList.SelectedItem != null && ListBoxPlayList.SelectedItem != HyPlayList.NowPlayingItem &&
                realSelectSong)
                HyPlayList.SongMoveTo(ListBoxPlayList.SelectedItem as HyPlayItem);
        }
    }

    public void ShowExpandedPlayer()
    {
        if (!HyPlayList.Player.PlayerCreated || HyPlayList.NowPlayingItem?.PlayItem?.AudioGraphPlaybackSource == null) return;
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
            catch (Exception)
            {
                //ignore
            }

        if (Common.Setting.forceMemoryGarbage)
            Common.NavigatePage(typeof(BlankPage));
        Common.isExpanded = true;
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
            ConnectedAnimation anim1 = null;
            ConnectedAnimation anim2 = null;
            ConnectedAnimation anim3 = null;
            ConnectedAnimation anim4 = null;
            anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
            anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
            anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
            anim4 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongAlbum");
            if (anim4 != null) anim4.Configuration = new DirectConnectedAnimationConfiguration();
            if (anim3 != null) anim3.Configuration = new DirectConnectedAnimationConfiguration();
            if (anim2 != null) anim2.Configuration = new DirectConnectedAnimationConfiguration();
            if (anim1 != null) anim1.Configuration = new DirectConnectedAnimationConfiguration();
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
        Common.isExpanded = false;
        RefreshPlayBarCover(HyPlayList.NowPlayingItem);
    }

    private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.ManualRemoveAllSong();
    }

    private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
    {
        _ = HyPlayList.PickLocalFile();
    }

    private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn)
            {
                var item = btn.DataContext as HyPlayItem;
                var index = HyPlayList.List.IndexOf(item);
                HyPlayList.RemoveSong(index);
                RefreshSongList();
            }
        }
        catch
        {
        }
    }

    private void BtnPlayRollType_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Common.IsInFm)
        {
            switch (NowPlayType)
            {
                case PlayMode.DefaultRoll:
                    //变成随机
                    HyPlayList.ChangePlayMode(PlayMode.Shuffled);
                    NowPlayType = PlayMode.Shuffled;
                    IconPlayType.Glyph = "\uE14B";
                    FlyoutPlayRollType.Text = "随机播放";
                    RefreshSongList();
                    break;
                case PlayMode.Shuffled:
                    //变成单曲
                    IconPlayType.Glyph = "\uE1CC";
                    HyPlayList.ChangePlayMode(PlayMode.SinglePlay);
                    NowPlayType = PlayMode.SinglePlay;
                    FlyoutPlayRollType.Text = "单曲循环";
                    break;
                case PlayMode.SinglePlay:
                    //变成顺序
                    HyPlayList.ChangePlayMode(PlayMode.DefaultRoll);
                    NowPlayType = PlayMode.DefaultRoll;
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
                    Id = HyPlayList.NowPlayingItem.PlayItem.Id
                });
            PersonalFM.LoadNextFM();
        }
    }

    private void BtnLike_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.LikeSong();
    }

    private void ImageContainer_OnTapped(object sender, RoutedEventArgs tappedRoutedEventArgs)
    {
        ButtonExpand_OnClick(sender, null);
    }

    private async void TbSingerName_OnTapped(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].Id);
                }
                else
                {
                    if (HyPlayList.NowPlayingItem.PlayItem.Artist.Count > 1)
                        await new ArtistSelectDialog(HyPlayList.NowPlayingItem.PlayItem.Artist).ShowAsync();
                    else
                        Common.NavigatePage(typeof(ArtistPage),
                            HyPlayList.NowPlayingItem.PlayItem.Artist[0].Id);
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
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                if (HyPlayList.NowPlayingItem.PlayItem.Artist[0].Type == HyPlayItemType.Radio)
                {
                    Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].Id);
                }
                else
                {
                    if (HyPlayList.NowPlayingItem.PlayItem.Album.Id != "0")
                        Common.NavigatePage(typeof(AlbumPage),
                            HyPlayList.NowPlayingItem.PlayItem.Album.Id);
                }
            }
        }
        catch
        {
        }
    }

    private async void Btn_Sub_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            await new SongListSelect(HyPlayList.NowPlayingItem.PlayItem.Id).ShowAsync();
    }

    private void Btn_Down_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.ItemType is HyPlayItemType.Netease or HyPlayItemType.Radio)
        {
            DownloadManager.AddDownload(HyPlayList.NowPlayingItem.ToNCSong());
        }
    }

    private void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            Common.NavigatePage(typeof(Comments), "sg" + HyPlayList.NowPlayingItem.PlayItem.Id);
        else
            Common.NavigatePage(typeof(Comments), "fm" + HyPlayList.NowPlayingItem.PlayItem.Album.Alias);
        if (Common.Setting.forceMemoryGarbage)
            Common.NavigatePage(typeof(BlankPage));
        CollapseExpandedPlayer();
    }

    private void Btn_Share_OnClick(object sender, RoutedEventArgs e)
    {
        //TODO: 分享电台节目
        if (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Netease) return;
        var dataTransferManager = DataTransferManager.GetForCurrentView();

        dataTransferManager.DataRequested += (manager, args) =>
        {
            var dataPackage = new DataPackage();
            dataPackage.SetWebLink(new Uri("https://music.163.com/#/song?Id=" +
                                           HyPlayList.NowPlayingItem.PlayItem.Id));
            dataPackage.Properties.Title = HyPlayList.NowPlayingItem.PlayItem.Name;
            dataPackage.Properties.Description =
                "歌手: " + string.Join(';',
                    HyPlayList.NowPlayingItem.PlayItem.Artist
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
        if (HyPlayList.NowPlaying >= 0 && HyPlayList.NowPlaying < PlayItems.Count)
            if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
                Common.Setting.displayShuffledList)
                // 新的随机算法
                ListBoxPlayList.ScrollIntoView(PlayItems[HyPlayList.ShufflingIndex]);
            else
                ListBoxPlayList.ScrollIntoView(PlayItems[HyPlayList.NowPlaying]);
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
        LoadPlayingFile(HyPlayList.NowPlayingItem);
        RefreshPlayBarCover(HyPlayList.NowPlayingItem);
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializedAni.Begin();
        PlayBarBackgroundFadeIn.Begin();
        HyPlayList.PlayerOutgoingVolume = (double)Common.Setting.Volume / 100;
        SliderAudioRate.Value = HyPlayList.PlayerOutgoingVolume * 100;
        HyPlayList.OnPlayItemChange += LoadPlayingFile;
        HyPlayList.OnPlayPositionChange += OnPlayPositionChange;
        HyPlayList.OnPlayListAddDone += RefreshSongList;
        HyPlayList.OnSongRemoveAll += HyPlayListOnOnSongRemoveAll;
        HyPlayList.OnLoginDone += HyPlayListOnOnLoginDone;
        HyPlayList.OnSongLikeStatusChange += HyPlayList_OnSongLikeStatusChange;
        HyPlayList.OnSongCoverChanged += RefreshPlayBarCover;
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
        Common.Logs.Add("Now PlaySource is " + HyPlayList.PlaySourceId);

        if (Common.isExpanded)
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
                if (Common.isExpanded && Common.Setting.playbarBackgroundElay)
                    GridThis.Background = BackgroundElayBrush;
            };
            PointerExited += (o, args) => { GridThis.Background = new SolidColorBrush(Colors.Transparent); };
        }

    }

    public async void RefreshPlayBarCover(HyPlayItem playItem)
    {
        if (HyPlayList.CoverStream == null) return;
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
        {
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
            {
                try
                {
                    if (playItem != HyPlayList.NowPlayingItem) return;
                    using var stream = HyPlayList.CoverStream.CloneStream();
                    await AlbumImageSource.SetSourceAsync(stream);
                }
                catch
                {
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

    private void HyPlayListOnOnLoginDone()
    {
        _ = Task.Run(async () =>
        {
            if (HyPlayList.PlaySourceId == "local") return;
            try
            {
                var list = await HistoryManagement.GetcurPlayingListHistory();
                if (list.Count > 0)
                {
                    int.TryParse(ApplicationData.Current.LocalSettings.Values["nowSongPointer"].ToString(),
                        out HyPlayList.NowPlaying);
                    HyPlayList.AppendNcSongs(list);
                    HyPlayList.NotifyPlayItemChanged(HyPlayList.NowPlayingItem);
                }

                list.Clear();
            }
            catch
            {
                // ignored
            }
        });

    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABStartPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABEndPoint = HyPlayList.Player.PrimaryAudioInputNode.Position;
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
            HyPlayList.Seek(value);
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
        HyPlayList.Seek(value);
    }

    private void CopySongDetailFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new DataPackage();
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
        HyPlayList.List.Reverse();
        HyPlayList.SongAppendDone();
        HyPlayList.NowPlaying = HyPlayList.List.Count - HyPlayList.NowPlaying - 1;
    }
}