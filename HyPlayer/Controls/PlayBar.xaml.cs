#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
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
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Toolkit.Uwp.UI.Media;
using NeteaseCloudMusicApi;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class PlayBar
{
    private bool canslide;
    public bool FadeSettedVolume;
    public PlayMode NowPlayType = PlayMode.DefaultRoll;
    public ObservableCollection<HyPlayItem> PlayItems = new();

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
        PlayItems.Clear();
        PlayListTitle.Text = "播放列表";
    }


    private void UpdateMSTC(TimeSpan pos)
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
        data.Values["Title"] = HyPlayList.NowPlayingItem.PlayItem.Name;
        data.Values["PureLyric"] = HyPlayList.Lyrics[HyPlayList.LyricPos].PureLyric;
        // TODO 此处有点冒险的报错,请注意测试
        data.Values["Translation"] = HyPlayList.Lyrics[HyPlayList.LyricPos].Translation is null
            ? HyPlayList.Lyrics.Count > HyPlayList.LyricPos + 1
                ? HyPlayList.Lyrics[HyPlayList.LyricPos + 1].PureLyric
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

    private void HyPlayList_OnPlayListAdd()
    {
        RefreshSongList();
        HistoryManagement.SetcurPlayingListHistory(HyPlayList.List
            .Where(t => t.ItemType == HyPlayItemType.Netease)
            .Select(t => t.PlayItem.Id).ToList());
    }

    public void OnPlayPositionChange(TimeSpan ts)
    {
        _ = Common.Invoke(() =>
        {
            try
            {
                if (HyPlayList.NowPlayingItem?.PlayItem == null) return;
                TbSingerName.Content = HyPlayList.NowPlayingItem.PlayItem.ArtistString;
                TbAlbumName.Content = HyPlayList.NowPlayingItem.PlayItem.AlbumString;
                TbSongName.Text = HyPlayList.NowPlayingItem.PlayItem.Name;
                canslide = false;
                SliderProgress.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
                canslide = true;
                TextBlockTotalTime.Text =
                    TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds)
                        .ToString(@"hh\:mm\:ss");
                TextBlockNowTime.Text =
                    HyPlayList.Player.PlaybackSession.Position.ToString(@"hh\:mm\:ss");
                PlayStateIcon.Glyph =
                    HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                        ? "\uEDB4"
                        : "\uEDB5";

                if (Common.Setting.fadeInOut)
                {
                    if (HyPlayList.Player.PlaybackSession.Position.TotalSeconds <= Common.Setting.fadeInOutTime)
                    {
                        FadeSettedVolume = true;
                        var vol = Common.Setting.Volume;
                        HyPlayList.Player.Volume = HyPlayList.Player.PlaybackSession.Position.TotalSeconds /
                            Common.Setting.fadeInOutTime * vol / 100;
                        Debug.WriteLine(HyPlayList.Player.PlaybackSession.Position.TotalSeconds /
                            Common.Setting.fadeInOutTime * vol / 100);
                    }
                    else if (HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 -
                             HyPlayList.Player.PlaybackSession.Position.TotalSeconds <=
                             Common.Setting.fadeInOutTime)
                    {
                        FadeSettedVolume = true;
                        HyPlayList.Player.Volume =
                            (HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 -
                             HyPlayList.Player.PlaybackSession.Position.TotalSeconds) /
                            Common.Setting.fadeInOutTime * Common.Setting.Volume / 100;
                        Debug.WriteLine(
                            (HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 -
                             HyPlayList.Player.PlaybackSession.Position.TotalSeconds) /
                            Common.Setting.fadeInOutTime * Common.Setting.Volume / 100);
                    }
                    else
                    {
                        FadeSettedVolume = false;
                    }
                }


                //SliderAudioRate.Value = mp.Volume;
            }
            catch
            {
                //ignore
            }
        });
    }


    public async void LoadPlayingFile(HyPlayItem mpi)
    {
        if (Common.IsInFm)
        {
            IconPrevious.Glyph = "\uE7E8";
            IconPlayType.Glyph = "\uE107";
            FlyoutPlayRollType.Text = "私人 FM";
        }
        else
        {
            IconPrevious.Glyph = "\uE892";
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

        if (HyPlayList.NowPlayingItem.PlayItem == null)
        {
            TbSingerName.Content = null;
            TbSongName.Text = null;
            TbAlbumName.Content = null;
            ApplicationView.GetForCurrentView().Title = "";
            TbSongTag.Text = "无歌曲";
            AlbumImage.Source = null;
            return;
        }

        TbSingerName.Content = HyPlayList.NowPlayingItem.PlayItem.ArtistString;
        TbSongName.Text = HyPlayList.NowPlayingItem.PlayItem.Name;
        TbAlbumName.Content = HyPlayList.NowPlayingItem.PlayItem.AlbumString;
        ApplicationView.GetForCurrentView().Title = HyPlayList.NowPlayingItem.PlayItem.ArtistString + " - " +
                                                    HyPlayList.NowPlayingItem.PlayItem.Name;
        try
        {
            if (!Common.Setting.noImage)
                if (mpi.ItemType == HyPlayItemType.Local)
                {
                    var img = new BitmapImage();
                    await img.SetSourceAsync(
                        await HyPlayList.NowPlayingStorageFile?.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999));
                    AlbumImage.Source = img;
                }
                else
                {
                    AlbumImage.Source =
                        new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover + "?param=" +
                                                StaticSource.PICSIZE_PLAYBAR_ALBUMCOVER));
                }
        }
        catch (Exception)
        {
            //IGNORE
        }

        //SliderAudioRate.Value = HyPlayList.Player.Volume * 100;
        canslide = false;
        SliderProgress.Minimum = 0;
        SliderProgress.Maximum = HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds;
        SliderProgress.Value = 0;
        canslide = true;
        bool isLiked = Common.LikedSongs.Contains(mpi.PlayItem.Id);
        if (mpi.ItemType != HyPlayItemType.Local)
        {
            IconLiked.Foreground = isLiked
                ? new SolidColorBrush(Colors.Red)
                : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
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
            HistoryManagement.AddNCSongHistory(mpi.PlayItem.Id);
        }

        realSelectSong = false;
        // 新版随机播放算法
        if (HyPlayList.NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating &&
            Common.Setting.displayShuffledList)
        {
            ListBoxPlayList.SelectedIndex = HyPlayList.ShufflingIndex;
        }
        else
        {
            ListBoxPlayList.SelectedIndex = HyPlayList.NowPlaying;
        }

        realSelectSong = true;
        if (HyPlayList.NowPlayingItem.PlayItem.Tag != "在线")
            TbSongTag.Text = HyPlayList.NowPlayingItem.PlayItem.Tag;
        Btn_Share.IsEnabled = HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease;
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
            targetingList.ForEach(t => PlayItems.Add(t));
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
            HyPlayList.LoadPlayerSong();
        if (HyPlayList.IsPlaying)
        {
            if (Common.Setting.fadeInOutPause)
            {
                FadeSettedVolume = true;
                var vol = Common.Setting.Volume;
                var curtime = HyPlayList.Player.PlaybackSession.Position.TotalSeconds;
                for (; ; )
                    try
                    {
                        await Task.Delay(50);
                        var curvol = (1 - (HyPlayList.Player.PlaybackSession.Position.TotalSeconds - curtime) /
                            (Common.Setting.fadeInOutTimePause / 10)) * vol / 100;
                        Debug.WriteLine(HyPlayList.Player.Volume);
                        if (curvol <= 0)
                        {
                            HyPlayList.Player.Volume = 0;
                            HyPlayList.Player.Pause();
                            HyPlayList.Player.Volume = (double)vol / 100;
                            FadeSettedVolume = false;
                            break;
                        }

                        HyPlayList.Player.Volume = curvol;
                    }
                    catch
                    {
                        HyPlayList.Player.Volume = 0;
                        HyPlayList.Player.Pause();
                        HyPlayList.Player.Volume = vol;
                        FadeSettedVolume = false;
                        break;
                    }
            }

            HyPlayList.Player.Pause();
            PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB5" : "\uEDB4";
            return;
        }

        if (!HyPlayList.IsPlaying)
        {
            HyPlayList.Player.Play();
            if (Common.Setting.fadeInOutPause)
            {
                FadeSettedVolume = true;
                var vol = Common.Setting.Volume;
                HyPlayList.Player.Volume = 0;
                var curtime = HyPlayList.Player.PlaybackSession.Position.TotalSeconds;
                for (; ; )
                {
                    await Task.Delay(50);
                    var curvol = (HyPlayList.Player.PlaybackSession.Position.TotalSeconds - curtime) /
                        (Common.Setting.fadeInOutTimePause / 10) * vol / 100;
                    if (curvol >= (double)vol / 100)
                    {
                        curvol = (double)vol / 100;
                        HyPlayList.Player.Volume = curvol;
                        break;
                    }

                    if (curtime < HyPlayList.Player.PlaybackSession.Position.TotalSeconds)
                        HyPlayList.Player.Volume = curvol;
                    else HyPlayList.Player.Volume = (double)vol / 100;
                    Debug.WriteLine(HyPlayList.Player.Volume);
                    if (HyPlayList.Player.Volume >= (double)vol / 100)
                    {
                        HyPlayList.Player.Volume = (double)vol / 100;
                        break;
                    }
                }

                FadeSettedVolume = false;
                PlayStateIcon.Glyph = HyPlayList.IsPlaying ? "\uEDB5" : "\uEDB4";
            }
        }
    }

    private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        HyPlayList.Player.Volume = e.NewValue / 100;
        //if (Common.PageExpandedPlayer != null) Common.PageExpandedPlayer.SliderVolumn.Value = e.NewValue;
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
        Common.PageMain.GridPlayBar.Background = null;
        //Common.PageMain.MainFrame.Visibility = Visibility.Collapsed;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer), null,
            new EntranceNavigationTransitionInfo());
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
        if (Common.PageExpandedPlayer == null) return;
        Common.PageExpandedPlayer.StartCollapseAnimation();
        GridSongAdvancedOperation.Visibility = Visibility.Collapsed;
        GridSongInfo.Visibility = Visibility.Visible;
        if (Common.Setting.expandAnimation && GridSongInfoContainer.Visibility == Visibility.Visible)
        {
            ConnectedAnimation anim1 = null;
            ConnectedAnimation anim2 = null;
            ConnectedAnimation anim3 = null;
            anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
            anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
            anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
            anim3.Configuration = new DirectConnectedAnimationConfiguration();
            if (anim2 != null) anim2.Configuration = new DirectConnectedAnimationConfiguration();

            anim1.Configuration = new DirectConnectedAnimationConfiguration();
            try
            {
                anim3?.TryStart(TbSingerName);
                anim1?.TryStart(TbSongName);
                anim2?.TryStart(AlbumImage);
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
        Common.PageExpandedPlayer.Dispose();
        Common.PageExpandedPlayer = null;
        Common.PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
        //Common.PageMain.MainFrame.Visibility = Visibility.Visible;
        Common.PageMain.ExpandedPlayer.Visibility = Visibility.Collapsed;
        if (Common.Setting.useAcrylic)
            Common.PageMain.GridPlayBar.Background =
                Application.Current.Resources["SystemControlAcrylicElementMediumHighBrush"] as Brush;
        else
            Common.PageMain.GridPlayBar.Background = new BackdropBlurBrush() { Amount = 30.0 };
        Window.Current.SetTitleBar(Common.PageBase.AppTitleBar);
        Common.isExpanded = false;
    }

    private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.ManualRemoveAllSong();
    }

    private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.PickLocalFile();
    }

    private void SliderProgress_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (canslide) HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(SliderProgress.Value);
    }

    private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn)
            {
                HyPlayList.RemoveSong(HyPlayList.List.FindIndex(t => t.PlayItem == btn.Tag));
                RefreshSongList();
            }
        }
        catch
        {
        }
    }

    private void BtnPlayRollType_OnClick(object sender, RoutedEventArgs e)
    {
        if (Common.IsInFm.ToString() != "true")
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
            _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.FmTrash,
                new Dictionary<string, object> { { "id", HyPlayList.NowPlayingItem.PlayItem.Id } });
            PersonalFM.LoadNextFM();
        }
    }

    private void BtnLike_OnClick(object sender, RoutedEventArgs e)
    {
        bool isLiked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.Id);
        switch (HyPlayList.NowPlayingItem.ItemType)
        {
            case HyPlayItemType.Netease:
                {
                    Api.LikeSong(HyPlayList.NowPlayingItem.PlayItem.Id,
                        !isLiked);
                    if (isLiked)
                        Common.LikedSongs.Remove(HyPlayList.NowPlayingItem.PlayItem.Id);
                    else
                        Common.LikedSongs.Add(HyPlayList.NowPlayingItem.PlayItem.Id);
                    isLiked = !isLiked;
                    IconLiked.Foreground = isLiked
                        ? new SolidColorBrush(Colors.Red)
                        : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
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
                    break;
                }
            case HyPlayItemType.Radio:
                _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.ResourceLike,
                    new Dictionary<string, object>
                        { { "type", "4" }, { "t", "1" }, { "id", HyPlayList.NowPlayingItem.PlayItem.Id } });
                break;
            default:
                IconLiked.Foreground = isLiked
                    ? new SolidColorBrush(Colors.Red)
                    : Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;
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
                break;
        }
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
        if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
        {
            DownloadManager.AddDownload(HyPlayList.NowPlayingItem.ToNCSong());
        }
        else if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Radio)
        {
            //TODO: 电台的下载操作
        }
    }

    private void Btn_Comment_OnClick(object sender, RoutedEventArgs e)
    {
        if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            Common.NavigatePage(typeof(Comments), "sg" + HyPlayList.NowPlayingItem.PlayItem.Id);
        else
            Common.NavigatePage(typeof(Comments), "fm" + HyPlayList.NowPlayingItem.PlayItem.Album.alias);
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
        if (!Common.Setting.toastLyric && Common.Setting.progressInSMTC)
        {
            // 当前未打开歌词
            Bindings.Update();
            try
            {
                var uri = new Uri($"hot-lyric:///?from={Windows.ApplicationModel.Package.Current.Id.FamilyName}");
                if (await Windows.System.Launcher.QueryUriSupportAsync(uri, Windows.System.LaunchQuerySupportType.Uri, "306200B4771A6.217957860C1A5_mb3g82vhcggpy") == Windows.System.LaunchQuerySupportStatus.Available)
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                    Common.Setting.toastLyric = false;
                    Bindings.Update();
                    return;
                }
            }
            catch { }
        }

        Common.Setting.toastLyric = !Common.Setting.toastLyric;
        InitializeDesktopLyric();
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
            {
                // 新的随机算法
                ListBoxPlayList.ScrollIntoView(PlayItems[HyPlayList.ShufflingIndex]);
            }
            else
            {
                ListBoxPlayList.ScrollIntoView(PlayItems[HyPlayList.NowPlaying]);
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

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        HyPlayList.Player.Volume = (double)Common.Setting.Volume / 100;
        SliderAudioRate.Value = HyPlayList.Player.Volume * 100;
        HyPlayList.OnPlayItemChange += LoadPlayingFile;
        HyPlayList.OnPlayPositionChange += OnPlayPositionChange;
        //HyPlayList.OnPlayPositionChange += UpdateMSTC;
        HyPlayList.OnPlayListAddDone += HyPlayList_OnPlayListAdd;
        HyPlayList.OnSongRemoveAll += HyPlayListOnOnSongRemoveAll;
        AlbumImage.Source = new BitmapImage(new Uri("ms-appx:Assets/icon.png"));
        if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
            ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
        InitializeDesktopLyric();
        realSelectSong = false;
        ListBoxPlayList.ItemsSource = PlayItems;
        realSelectSong = true;

        try
        {
            var list = await HistoryManagement.GetcurPlayingListHistory();
            HyPlayList.AppendNcSongs(list);
            if (list.Count > 0)
            {
                int.TryParse(ApplicationData.Current.LocalSettings.Values["nowSongPointer"].ToString(),
                    out HyPlayList.NowPlaying);
                HyPlayList.Player_SourceChanged(null, null);
                HyPlayList.SongAppendDone();
            }
        }
        catch
        {
        }

        if (Common.isExpanded)
            Common.BarPlayBar.ShowExpandedPlayer();
        if (!Common.Setting.useAcrylic)
            Common.PageMain.GridPlayBar.Background = new BackdropBlurBrush() { Amount = 30.0 };
        if (Common.Setting.hotlyricOnStartup)
        {
            try
            {
                var uri = new Uri($"hot-lyric:///?from={Windows.ApplicationModel.Package.Current.Id.FamilyName}");
                if (await Windows.System.Launcher.QueryUriSupportAsync(uri, Windows.System.LaunchQuerySupportType.Uri, "306200B4771A6.217957860C1A5_mb3g82vhcggpy") == Windows.System.LaunchQuerySupportStatus.Available)
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                    Common.Setting.toastLyric = false;
                    Bindings.Update();
                    return;
                }
            }
            catch { }
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
}

public class ThumbConverter : DependencyObject, IValueConverter
{
    // Using a DependencyProperty as the backing store for SecondValue.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SecondValueProperty =
        DependencyProperty.Register("SecondValue", typeof(double), typeof(ThumbConverter),
            new PropertyMetadata(0d));

    public double SecondValue
    {
        get => (double)GetValue(SecondValueProperty);
        set => SetValue(SecondValueProperty, value);
    }


    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // assuming you want to display precentages

        return TimeSpan.FromMilliseconds(double.Parse(value.ToString())).ToString(@"hh\:mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}