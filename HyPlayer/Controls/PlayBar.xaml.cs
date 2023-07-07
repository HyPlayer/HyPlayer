﻿#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using Microsoft.Toolkit.Uwp.Notifications;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Profile;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlayBar
{
    private SolidColorBrush BackgroundElayBrush = new(Colors.Transparent);
    private bool _isSliding = false;
    public PlayMode NowPlayType = PlayMode.DefaultRoll;
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
    }

    public TimeSpan nowtime => HyPlayList.Player.PlaybackSession.Position;

    private void HyPlayListOnOnSongRemoveAll()
    {
        _ = Common.Invoke(() =>
        {
            PlayItems.Clear();
            PlayListTitle.Text = "播放列表";
        });
    }


    private void UpdateSMTC(TimeSpan pos)
    {
        // Create our timeline properties object 
        var timelineProperties = new SystemMediaTransportControlsTimelineProperties();

        // Fill in the data, using the media elements properties 
        timelineProperties.StartTime = TimeSpan.FromSeconds(0);
        timelineProperties.MinSeekTime = TimeSpan.FromSeconds(0);
        timelineProperties.Position = HyPlayList.Player.PlaybackSession.Position;
        timelineProperties.MaxSeekTime =
            TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds);
        timelineProperties.EndTime =
            TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds);
        // Update the System Media transport Controls 
        HyPlayList.MediaSystemControls.UpdateTimelineProperties(timelineProperties);
    }

    public void InitializeDesktopLyric()
    {
        if (Common.Setting.toastLyric)
        {
            var desktopLyricsToast = new ToastContentBuilder();
            desktopLyricsToast.SetToastScenario(ToastScenario.IncomingCall);
            desktopLyricsToast.AddAudio(new ToastAudio { Silent = true });
            desktopLyricsToast.AddVisualChild(new AdaptiveText
            {
                Text = new BindableString("Title"),
                HintStyle = AdaptiveTextStyle.Header
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveText
            {
                Text = new BindableString("PureLyric")
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveText
            {
                Text = new BindableString("Translation")
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveProgressBar
            {
                ValueStringOverride = new BindableString("TotalValueString"),

                Status = new BindableString("CurrentValueString"),

                Value = new BindableProgressBarValue("CurrentValue")
            });
            var toast = new ToastNotification(desktopLyricsToast.GetXml())
            {
                Tag = "HyPlayerDesktopLyrics",
                Data = new NotificationData()
            };
            toast.Data.Values["Title"] = "当前无音乐播放";
            toast.Data.Values["PureLyric"] = "当前无歌词";
            toast.Data.Values["TotalValueString"] = "0:00:00";
            toast.Data.Values["CurrentValueString"] = "0:00:00";
            toast.Data.Values["CurrentValue"] = "0";

            toast.Data.SequenceNumber = 0;
            toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(60);
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
            toast.Dismissed += Toast_Dismissed;
            HyPlayList.OnPlayPositionChange += FreshDesktopLyric;
        }
        else
        {
            HyPlayList.OnPlayPositionChange -= FreshDesktopLyric;
            var notifier = ToastNotificationManager.CreateToastNotifier();
            ToastNotificationManagerCompat.History.Clear();
        }
    }

    private void Toast_Dismissed(ToastNotification sender, ToastDismissedEventArgs args)
    {
        if (args.Reason == ToastDismissalReason.TimedOut)
        {
            var notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(sender);
        }
        else if (Common.Setting.toastLyric)
        {
            Common.Setting.toastLyric = false;
        }
    }

    private void FreshDesktopLyric(TimeSpan ts)
    {
        var data = new NotificationData
        {
            SequenceNumber = 0
        };
        if (HyPlayList.NowPlayingItem.PlayItem == null) return;
        data.Values["Title"] = HyPlayList.NowPlayingItem.PlayItem.Name;
        data.Values["PureLyric"] = HyPlayList.Lyrics[HyPlayList.LyricPos].LyricLine.CurrentLyric;
        // TODO 此处有点冒险的报错,请注意测试
        data.Values["Translation"] = HyPlayList.Lyrics[HyPlayList.LyricPos].Translation is null
            ? HyPlayList.Lyrics.Count > HyPlayList.LyricPos + 1
                ? HyPlayList.Lyrics[HyPlayList.LyricPos + 1].LyricLine.CurrentLyric
                : ""
            : HyPlayList.Lyrics[HyPlayList.LyricPos].Translation;
        data.Values["TotalValueString"] =
            TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds)
                .ToString(@"hh\:mm\:ss");
        data.Values["CurrentValueString"] = HyPlayList.Player.PlaybackSession.Position.ToString(@"hh\:mm\:ss");
        data.Values["CurrentValue"] = (HyPlayList.Player.PlaybackSession.Position /
                                       TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem
                                           .LengthInMilliseconds)).ToString();
        var res = ToastNotificationManager.CreateToastNotifier()
            .Update(data, "HyPlayerDesktopLyrics");
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
                    SliderProgress.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
                }

                if (HyPlayList.Player.PlaybackSession.Position.Hours == 0)
                {
                    if (HyPlayList.Player.PlaybackSession.Position.Minutes < 10)
                        TextBlockNowTime.Text =
                            HyPlayList.Player.PlaybackSession.Position.ToString(@"m\:ss");
                    else
                        TextBlockNowTime.Text =
                            HyPlayList.Player.PlaybackSession.Position.ToString(@"mm\:ss");
                }
                else
                {
                    TextBlockNowTime.Text =
                        HyPlayList.Player.PlaybackSession.Position.ToString(@"hh\:mm\:ss");
                }
                if (HyPlayList.FadeProcessStatus && !HyPlayList.AutoFadeProcessing)
                {
                    PlayStateIcon.Glyph =
                    HyPlayList.CurrentFadeInOutState == HyPlayList.FadeInOutState.FadeIn
                        ? "\uF8AE"
                        : "\uF5B0";
                }
                else
                {
                    PlayStateIcon.Glyph =
                    HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                        ? "\uF8AE"
                        : "\uF5B0";
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
            SliderProgress.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;

            TextBlockNowTime.Text =
                HyPlayList.Player.PlaybackSession.Position.ToString(@"m\:ss");
            PlayStateIcon.Glyph =
                HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                    ? "\uF8AE" :
                    "\uF5B0";

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

            if (HyPlayList.NowPlayingItem.PlayItem.Tag != "在线")
                TbSongTag.Text = HyPlayList.NowPlayingItem.PlayItem.Tag;
            Btn_Share.IsEnabled =
                HyPlayList.NowPlayingItem.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive;
        });
        var isLiked = Common.LikedSongs.Contains(mpi.PlayItem.Id);
        if (mpi.ItemType is not HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            _ = Common.Invoke(() =>
            {
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : IconPrevious.Foreground;
                FlyoutLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
                IconLiked.Glyph = isLiked
                    ? "\uE00B"
                    : "\uE006";
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

    public void RefreshSongList()
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

    private async void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.PlayItem?.Name != null && HyPlayList.Player.Source == null)
            _ = HyPlayList.LoadPlayerSong(HyPlayList.List[HyPlayList.NowPlaying]);
        PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uF8AE" : "\uF5B0";
        if (HyPlayList.IsPlaying)
        {
            await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.PauseFadeOut);

            PlayBarBackgroundAni.Stop();
        }
        else
        {
            //HyPlayList.Player.Play();
            await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.PlayFadeIn);

            if (Common.Setting.playbarBackgroundBreath)
                PlayBarBackgroundAni.Begin();
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

    private async void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
    {
        if (Common.IsInFm)
            PersonalFM.ExitFm();
        else
            await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.UserNextFadeOut, HyPlayList.SongChangeType.Previous);

    }

    private async void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
    {
        await HyPlayList.SongFadeRequest(HyPlayList.SongFadeEffectType.UserNextFadeOut, HyPlayList.SongChangeType.Next);
    }

    private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
            Common.Setting.displayShuffledList)
        {
            if (ListBoxPlayList.SelectedIndex != -1 &&
                ListBoxPlayList.SelectedIndex != HyPlayList.ShuffleList[ListBoxPlayList.SelectedIndex] &&
                realSelectSong)
            {
                HyPlayList.SongMoveTo(HyPlayList.ShuffleList[ListBoxPlayList.SelectedIndex]);
                HyPlayList.ShufflingIndex = ListBoxPlayList.SelectedIndex;
            }
        }
        else
        {
            if (ListBoxPlayList.SelectedIndex != -1 && ListBoxPlayList.SelectedIndex != HyPlayList.NowPlaying &&
                realSelectSong)
                HyPlayList.SongMoveTo(ListBoxPlayList.SelectedIndex);
        }
    }

    public void ShowExpandedPlayer()
    {
        ButtonExpand.Visibility = Visibility.Collapsed;
        ButtonCollapse.Visibility = Visibility.Visible;
        PlayBarBackgroundFadeOut.Begin();
        //Common.PageMain.MainFrame.Visibility = Visibility.Collapsed;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer), null,
            new EntranceNavigationTransitionInfo());
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

    public async void ButtonCollapse_OnClick(object sender, RoutedEventArgs e)
    {
        await CollapseExpandedPlayer();
    }

    public async Task CollapseExpandedPlayer()
    {
        Common.PageMain.IsExpandedPlayerInitialized = false;
        if (Common.PageExpandedPlayer == null) return;
        Common.PageExpandedPlayer.StartCollapseAnimation();
        GridSongAdvancedOperation.Visibility = Visibility.Collapsed;
        GridSongInfo.Visibility = Visibility.Visible;
        PlayBarBackgroundFadeIn.Begin();
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
        Common.PageExpandedPlayer.Dispose();
        Common.PageExpandedPlayer = null;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
        //Common.PageMain.MainFrame.Visibility = Visibility.Visible;
        Common.PageMain.MainFrame.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Collapsed;
        Window.Current.SetTitleBar(Common.PageBase.AppTitleBar);
        Common.isExpanded = false;
        using var coverStream = HyPlayList.CoverStream.CloneStream();
        await RefreshPlayBarCover(HyPlayList.NowPlayingHashCode, coverStream);
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
                    HyPlayList.NowPlayType = PlayMode.Shuffled;
                    NowPlayType = PlayMode.Shuffled;
                    IconPlayType.Glyph = "\uE14B";
                    FlyoutPlayRollType.Text = "随机播放";
                    break;
                case PlayMode.Shuffled:
                    //变成单曲
                    IconPlayType.Glyph = "\uE1CC";
                    HyPlayList.NowPlayType = PlayMode.SinglePlay;
                    NowPlayType = PlayMode.SinglePlay;
                    FlyoutPlayRollType.Text = "单曲循环";
                    break;
                case PlayMode.SinglePlay:
                    //变成顺序
                    HyPlayList.NowPlayType = PlayMode.DefaultRoll;
                    NowPlayType = PlayMode.DefaultRoll;
                    IconPlayType.Glyph = "\uE169";
                    FlyoutPlayRollType.Text = "顺序播放";
                    break;
            }
        }
        else
        {
            _ = Common.ncapi?.RequestAsync(CloudMusicApiProviders.FmTrash,
                new Dictionary<string, object> { { "id", HyPlayList.NowPlayingItem.PlayItem.Id } });
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
                    Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
                }
                else
                {
                    if (HyPlayList.NowPlayingItem.PlayItem.Artist.Count > 1)
                        await new ArtistSelectDialog(HyPlayList.NowPlayingItem.PlayItem.Artist).ShowAsync();
                    else
                        Common.NavigatePage(typeof(ArtistPage),
                            HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
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
                    Common.NavigatePage(typeof(Me), HyPlayList.NowPlayingItem.PlayItem.Artist[0].id);
                }
                else
                {
                    if (HyPlayList.NowPlayingItem.PlayItem.Album.id != "0")
                        Common.NavigatePage(typeof(AlbumPage),
                            HyPlayList.NowPlayingItem.PlayItem.Album.id);
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

    private async void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            Common.NavigatePage(typeof(Comments), "sg" + HyPlayList.NowPlayingItem.PlayItem.Id);
        else
            Common.NavigatePage(typeof(Comments), "fm" + HyPlayList.NowPlayingItem.PlayItem.Album.alias);
        if (Common.Setting.forceMemoryGarbage)
            Common.NavigatePage(typeof(BlankPage));
        await CollapseExpandedPlayer();
    }

    private void Btn_Share_OnClick(object sender, RoutedEventArgs e)
    {
        //TODO: 分享电台节目
        if (HyPlayList.NowPlayingItem.ItemType != HyPlayItemType.Netease) return;
        var dataTransferManager = DataTransferManager.GetForCurrentView();

        dataTransferManager.DataRequested += (manager, args) =>
        {
            var dataPackage = new DataPackage();
            dataPackage.SetWebLink(new Uri("https://music.163.com/#/song?id=" +
                                           HyPlayList.NowPlayingItem.PlayItem.Id));
            dataPackage.Properties.Title = HyPlayList.NowPlayingItem.PlayItem.Name;
            dataPackage.Properties.Description =
                "歌手: " + string.Join(';',
                    HyPlayList.NowPlayingItem.PlayItem.Artist
                        .Select(t => t.name));
            var request = args.Request;
            request.Data = dataPackage;
        };

        //展示系统的共享ui
        DataTransferManager.ShowShareUI();
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (Common.Setting.toastLyric)
        {
            Common.Setting.toastLyric = false;
            InitializeDesktopLyric();
            return;
        }

        if (Common.Setting.noUseHotLyric)
        {
            Common.Setting.toastLyric = true;
            InitializeDesktopLyric();
            return;
        }

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
                    "目前 HyPlayer 已经适配「热词」，我们推荐使用「热词」来获得真正的桌面歌词体验。\r\n同时我们仍然保留了旧的 Toast 歌词\r\n如想使用 Toast 歌词请点击否。\r\n或者可以前往 Microsoft 商店安装 「热词」",
                CloseButtonText = "否",
                PrimaryButtonText = "安装 「热词」"
            };

            var res = await dlg.ShowAsync(ContentDialogPlacement.Popup);
            if (res == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9"));
                return;
            }

            Common.Setting.toastLyric = true;
            InitializeDesktopLyric();
            return;
        }

        try
        {
            if (!Common.Setting.progressInSMTC) Common.Setting.progressInSMTC = true;
            await Launcher.LaunchUriAsync(uri, new LauncherOptions
            {
                FallbackUri = new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9")
            });
            Common.Setting.toastLyric = false;
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
    private async Task OnEnteringForeground()
    {
        LoadPlayingFile(HyPlayList.NowPlayingItem);
        using var coverStream = HyPlayList.CoverStream.CloneStream();
        await RefreshPlayBarCover(HyPlayList.NowPlayingHashCode, coverStream);
    }
    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        InitializedAni.Begin();
        PlayBarBackgroundFadeIn.Begin();
        HyPlayList.PlayerOutgoingVolume = (double)Common.Setting.Volume / 100;
        SliderAudioRate.Value = HyPlayList.PlayerOutgoingVolume * 100;
        HyPlayList.OnPlayItemChange += LoadPlayingFile;
        HyPlayList.OnPlayPositionChange += OnPlayPositionChange;
        //HyPlayList.OnPlayPositionChange += UpdateMSTC;
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
            BtnPlayStateChange.Background = Resources["SolidPlayButtonColor"] as Brush;
            PlayStateIcon.Foreground = Resources["SolidPlayButtonIconColor"] as Brush;
        }
        else
            PlayBarBackgroundAni.Children.RemoveAt(2);
        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
        InitializeDesktopLyric();
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
                        Common.Setting.toastLyric = false;
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

        /*
        verticalAnimation = new DoubleAnimation();

        verticalAnimation.From = 0;
        verticalAnimation.To = 0;
        verticalAnimation.SpeedRatio = 0.1;
        verticalAnimation.Duration = new Duration(TimeSpan.FromSeconds(4));
        verticalAnimation.AutoReverse = true;
        verticalAnimation.RepeatBehavior = RepeatBehavior.Forever;
        verticalAnimation.EnableDependentAnimation = true;

        TbSongNameScrollStoryBoard = new Storyboard();
        TbSongNameScrollStoryBoard.Children.Add(verticalAnimation);
        Storyboard.SetTarget(verticalAnimation, TbSongName);
        Storyboard.SetTargetProperty(verticalAnimation, "Horizontalofset");
        TbSongNameScrollStoryBoard.Begin();
        */
    }
    public async Task RefreshPlayBarCover(int hashCode, IRandomAccessStream coverStream)
    {
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
        {
            using var stream = coverStream.CloneStream();
            if (GridSongInfo.Visibility == Visibility.Visible && Opacity != 0)
            {
                try
                {
                    if (stream.Size != 0)
                    {
                        if (hashCode != HyPlayList.NowPlayingHashCode) return;
                        await AlbumImageSource.SetSourceAsync(stream);
                    }
                }
                catch
                {

                }
            }
        });
    }

    private void HyPlayList_OnSongLikeStatusChange(bool isLiked)
    {
        IconLiked.Foreground = isLiked
            ? new SolidColorBrush(Colors.Red)
            : IconPrevious.Foreground;
        FlyoutLiked.Foreground = isLiked
            ? new SolidColorBrush(Colors.Red)
            : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
        IconLiked.Glyph = isLiked
            ? "\uE00B"
            : "\uE006";
        FlyoutLiked.Glyph = isLiked
            ? "\uE00B"
            : "\uE006";
    }

    private async void HyPlayListOnOnLoginDone()
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
    }

    private void SetABStartPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABStartPoint = HyPlayList.Player.PlaybackSession.Position;
    }

    private void SetABEndPointButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABEndPoint = HyPlayList.Player.PlaybackSession.Position;
    }

    private void ABRepeatStateButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.ABRepeatStatus = !Common.Setting.ABRepeatStatus;
    }

    private void SliderProgress_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _slidingEventArgs = null;
        HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(SliderProgress.Value);
        _isSliding = false;
    }

    private void SliderProgress_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        _isSliding = true;
        _slidingEventArgs = e;
    }

    private void SliderProgress_OnManipulationStarting(object sender, ManipulationStartingRoutedEventArgs e)
    {
        HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(SliderProgress.Value);
    }

    private void CopySongDetailFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new DataPackage();
        switch ((sender as MenuFlyoutItem).Name)
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