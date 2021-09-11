using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.UI.Notifications;
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
using NeteaseCloudMusicApi;
using Windows.Storage;
using System.IO;
using Windows.Storage.Streams;
using System.Threading.Tasks;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class PlayBar : UserControl
    {
        private bool canslide;

        public PlayMode NowPlayType = PlayMode.DefaultRoll;
        private bool realSelectSong;
        public bool FadeSettedVolume = false;
        /*
        private Storyboard TbSongNameScrollStoryBoard;
        private double lastOffsetX;
        DoubleAnimation verticalAnimation;
        */

        public PlayBar()
        {
            Common.BarPlayBar = this;
            InitializeComponent();
            HyPlayList.Player.Volume = (double)Common.Setting.Volume / 100;
            SliderAudioRate.Value = HyPlayList.Player.Volume * 100;
            HyPlayList.OnPlayItemChange += LoadPlayingFile;
            HyPlayList.OnPlayPositionChange += OnPlayPositionChange;
            //HyPlayList.OnPlayPositionChange += UpdateMSTC;
            HyPlayList.OnPlayListAddDone += HyPlayList_OnPlayListAdd;
            AlbumImage.Source = new BitmapImage(new Uri("ms-appx:Assets/icon.png"));
            if (Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox")
                ButtonDesktopLyrics.Visibility = Visibility.Collapsed;
            InitializeDesktopLyric();
            Common.Invoke(async () =>
            {
                try
                {
                    var list = await HistoryManagement.GetcurPlayingListHistory();
                    await HyPlayList.AppendNCSongs(list);
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
            });
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


        public TimeSpan nowtime => HyPlayList.Player.PlaybackSession.Position;

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
            data.Values["PureLyric"] = HyPlayList.Lyrics[HyPlayList.lyricpos].PureLyric;
            // TODO 此处有点冒险的报错,请注意测试
            data.Values["Translation"] = HyPlayList.Lyrics[HyPlayList.lyricpos].Translation is null
                ? ((HyPlayList.Lyrics.Count > HyPlayList.lyricpos + 1)
                    ? HyPlayList.Lyrics[HyPlayList.lyricpos + 1].PureLyric
                    : "")
                : HyPlayList.Lyrics[HyPlayList.lyricpos].Translation;
            data.Values["TotalValueString"] =
                TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds).ToString(@"hh\:mm\:ss");
            data.Values["CurrentValueString"] = HyPlayList.Player.PlaybackSession.Position.ToString(@"hh\:mm\:ss");
            data.Values["CurrentValue"] = (HyPlayList.Player.PlaybackSession.Position /
                                           TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds)).ToString();
            var res = ToastNotificationManager.CreateToastNotifier()
                .Update(data, "HyPlayerDesktopLyrics");
        }

        private void HyPlayList_OnPlayListAdd()
        {
            RefreshSongList();
            HistoryManagement.SetcurPlayingListHistory(HyPlayList.List
                    .Where(t => t.ItemType == HyPlayItemType.Netease).Select(t => t.PlayItem.id).ToList());
        }

        private async void TestFile()
        {
            var fop = new FileOpenPicker();
            fop.FileTypeFilter.Add(".flac");
            fop.FileTypeFilter.Add(".mp3");
            fop.FileTypeFilter.Add(".ncm");


            var files =
                await fop.PickMultipleFilesAsync();
            HyPlayList.RemoveAllSong();

            foreach (var file in files)
            {
                if (Path.GetExtension(file.Path) == ".ncm")
                {
                    //脑残Music
                    Stream stream = await file.OpenStreamForReadAsync();
                    if (NCMFile.IsCorrectNCMFile(stream))
                    {
                        var Info = NCMFile.GetNCMMusicInfo(stream);
                        var hyitem = new HyPlayItem
                        {
                            ItemType = HyPlayItemType.Netease,
                            PlayItem = new PlayItem
                            {
                                DontSetLocalStorageFile = file,
                                Album = new NCAlbum
                                {
                                    name = Info.album,
                                    id = Info.albumId.ToString(),
                                    cover = Info.albumPic
                                },
                                url = file.Path,
                                subext = Info.format,
                                bitrate = Info.bitrate,
                                isLocalFile = true,
                                Type = HyPlayItemType.Netease,
                                LengthInMilliseconds = Info.duration,
                                id = Info.musicId.ToString(),
                                Artist = null,
                                /*
                                size = sf.GetBasicPropertiesAsync()
                                    .GetAwaiter()
                                    .GetResult()
                                    .Size.ToString(),
                                */
                                Name = Info.musicName,
                                tag = file.Provider.DisplayName + " NCM"
                            }
                        };
                        hyitem.PlayItem.Artist = Info.artist.Select(t => new NCArtist { name = t[0].ToString(), id = t[1].ToString() })
                            .ToList();

                        HyPlayList.List.Add(hyitem);
                    }
                    stream.Dispose();
                }
                else
                {
                    await HyPlayList.AppendStorageFile(file);
                }
            }

            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }

        public void OnPlayPositionChange(TimeSpan ts)
        {
            Common.Invoke((Action)(() =>
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
                       TimeSpan.FromMilliseconds(HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds).ToString(@"hh\:mm\:ss");
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
                           int vol = Common.Setting.Volume;
                           HyPlayList.Player.Volume = HyPlayList.Player.PlaybackSession.Position.TotalSeconds / Common.Setting.fadeInOutTime * vol / 100;
                           System.Diagnostics.Debug.WriteLine(HyPlayList.Player.PlaybackSession.Position.TotalSeconds / Common.Setting.fadeInOutTime * vol / 100);
                       }
                       else if (HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 - HyPlayList.Player.PlaybackSession.Position.TotalSeconds <= Common.Setting.fadeInOutTime)
                       {
                           FadeSettedVolume = true;
                           HyPlayList.Player.Volume = (HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 - HyPlayList.Player.PlaybackSession.Position.TotalSeconds) / Common.Setting.fadeInOutTime * Common.Setting.Volume / 100;
                           System.Diagnostics.Debug.WriteLine((HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds / 1000 - HyPlayList.Player.PlaybackSession.Position.TotalSeconds) / Common.Setting.fadeInOutTime * Common.Setting.Volume / 100);
                       }
                       else
                           FadeSettedVolume = false;
                   }
                   
                  
                   //SliderAudioRate.Value = mp.Volume;
               }
               catch
               {
                   //ignore
               }
           }));
        }


        public void LoadPlayingFile(HyPlayItem mpi)
        {
            if (Common.GLOBAL["PERSONALFM"].ToString() == "true")
            {
                IconPrevious.Glyph = "\uE7E8";
                IconPlayType.Glyph = "\uE107";
            }
            else
            {
                IconPrevious.Glyph = "\uE892";
                switch (HyPlayList.NowPlayType)
                {
                    case PlayMode.Shuffled:
                        //随机
                        IconPlayType.Glyph = "\uE14B";
                        break;
                    case PlayMode.SinglePlay:
                        //单曲
                        IconPlayType.Glyph = "\uE1CC";
                        break;
                    case PlayMode.DefaultRoll:
                        //顺序
                        IconPlayType.Glyph = "\uE169";
                        break;
                }
            }

            if (HyPlayList.NowPlayingItem.PlayItem == null) return;

            Common.Invoke(async () =>
            {
                TbSingerName.Content = HyPlayList.NowPlayingItem.PlayItem.ArtistString;
                TbSongName.Text = HyPlayList.NowPlayingItem.PlayItem.Name;
                TbAlbumName.Content = HyPlayList.NowPlayingItem.PlayItem.AlbumString;
                if (mpi.ItemType == HyPlayItemType.Local)
                {
                    var img = new BitmapImage();
                    await img.SetSourceAsync(
                        await HyPlayList.NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999));
                    AlbumImage.Source = img;
                }
                else
                {
                    AlbumImage.Source =
                        new BitmapImage(new Uri(HyPlayList.NowPlayingItem.PlayItem.Album.cover + "?param=" +
                                                StaticSource.PICSIZE_PLAYBAR_ALBUMCOVER));
                }

                //SliderAudioRate.Value = HyPlayList.Player.Volume * 100;
                canslide = false;
                SliderProgress.Minimum = 0;
                SliderProgress.Maximum = HyPlayList.NowPlayingItem.PlayItem.LengthInMilliseconds;
                SliderProgress.Value = 0;
                canslide = true;
                if (mpi.ItemType == HyPlayItemType.Netease)
                {
                    BtnLike.IsChecked = Common.LikedSongs.Contains(mpi.PlayItem.id);
                    HistoryManagement.AddNCSongHistory(mpi.PlayItem.id);
                }

                realSelectSong = false;
                ListBoxPlayList.SelectedIndex = HyPlayList.NowPlaying;
                realSelectSong = true;
                TbSongTag.Text = HyPlayList.NowPlayingItem.PlayItem.tag;
                Btn_Share.IsEnabled = HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease;
                /*
                verticalAnimation.To = TbSongName.ActualWidth - TbSongName.Tb.ActualWidth;
                verticalAnimation.SpeedRatio = 0.1;
                TbSongNameScrollStoryBoard.Stop();
                TbSongNameScrollStoryBoard.Children.Clear();
                TbSongNameScrollStoryBoard.Children.Add(verticalAnimation);
                TbSongNameScrollStoryBoard.Begin();
                */
            });
        }

        public void RefreshSongList()
        {
            try
            {
                var Contacts = new ObservableCollection<ListViewPlayItem>();
                for (var i = 0; i < HyPlayList.List.Count; i++)
                    Contacts.Add(new ListViewPlayItem(HyPlayList.List[i].PlayItem.Name, i,
                        HyPlayList.List[i].PlayItem.ArtistString));

                realSelectSong = false;
                ListBoxPlayList.ItemsSource = Contacts;
                ListBoxPlayList.SelectedIndex = HyPlayList.NowPlaying;
                realSelectSong = true;
                PlayListTitle.Text = "播放列表 (共" + HyPlayList.List.Count + "首)";
            }
            catch
            {
            }
        }

        private async void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.NowPlayingItem.PlayItem?.Name != null && HyPlayList.Player.Source == null)
                HyPlayList.LoadPlayerSong();
            if (HyPlayList.isPlaying)
            {
                if (Common.Setting.fadeInOutPause)
                {
                    FadeSettedVolume = true;
                    int vol = Common.Setting.Volume;
                    double curtime = HyPlayList.Player.PlaybackSession.Position.TotalSeconds;
                    for (; ; )
                    {
                        try
                        {
                            await Task.Delay(50);
                            double curvol = (1 - (HyPlayList.Player.PlaybackSession.Position.TotalSeconds - curtime) / (Common.Setting.fadeInOutTimePause / 10)) * vol / 100;
                            System.Diagnostics.Debug.WriteLine(HyPlayList.Player.Volume);
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
                }
                HyPlayList.Player.Pause();
                PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB5" : "\uEDB4";
                return;
            }
            else if (!HyPlayList.isPlaying)
            {
                HyPlayList.Player.Play();
                if (Common.Setting.fadeInOutPause)
                {
                    FadeSettedVolume = true;
                    int vol = Common.Setting.Volume;
                    HyPlayList.Player.Volume = 0;
                    double curtime = HyPlayList.Player.PlaybackSession.Position.TotalSeconds;
                    for (; ; )
                    {
                        await Task.Delay(50);
                        double curvol = (HyPlayList.Player.PlaybackSession.Position.TotalSeconds - curtime) / (Common.Setting.fadeInOutTimePause / 10) * vol / 100;
                        if (curvol >= (double)vol / 100)
                        {
                            curvol = (double)vol / 100;
                            HyPlayList.Player.Volume = curvol;
                            break;
                        }
                        if (curtime < HyPlayList.Player.PlaybackSession.Position.TotalSeconds)
                            HyPlayList.Player.Volume = curvol;
                        else HyPlayList.Player.Volume = (double)vol / 100;
                        System.Diagnostics.Debug.WriteLine(HyPlayList.Player.Volume);
                        if (HyPlayList.Player.Volume >= (double)vol / 100)
                        {
                            HyPlayList.Player.Volume = (double)vol / 100;
                            break;

                        }

                    }
                    FadeSettedVolume = false;
                    PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB5" : "\uEDB4";
                    return;
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
            //SliderAudioRate.Visibility = HyPlayList.Player.IsMuted ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
        {
            if (Common.GLOBAL["PERSONALFM"].ToString() == "true")
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
            if (ListBoxPlayList.SelectedIndex != -1 && ListBoxPlayList.SelectedIndex != HyPlayList.NowPlaying &&
                realSelectSong)
                HyPlayList.SongMoveTo(ListBoxPlayList.SelectedIndex);
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
            {
                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TbSongName);
                if (GridSongInfoContainer.Visibility == Visibility.Visible)
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", AlbumImage);

                ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TbSingerName);
                Common.PageExpandedPlayer.StartExpandAnimation();
            }

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

            Common.NavigateBack();
            ButtonExpand.Visibility = Visibility.Visible;
            ButtonCollapse.Visibility = Visibility.Collapsed;
            Common.PageExpandedPlayer.Dispose();
            Common.PageExpandedPlayer = null;
            Common.PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
            //Common.PageMain.MainFrame.Visibility = Visibility.Visible;
            Common.PageMain.ExpandedPlayer.Visibility = Visibility.Collapsed;
            Common.PageMain.GridPlayBar.Background =
                Application.Current.Resources["SystemControlAcrylicElementMediumHighBrush"] as Brush;
            Window.Current.SetTitleBar(Common.PageBase.AppTitleBar);
            Common.isExpanded = false;
        }

        private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.RemoveAllSong();
            ListBoxPlayList.ItemsSource = new ObservableCollection<ListViewPlayItem>();
            HyPlayList.SongAppendDone();
        }

        private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
        {
            TestFile();
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
                    HyPlayList.RemoveSong(int.Parse(btn.Tag.ToString()));
                    RefreshSongList();
                }
            }
            catch
            {
            }
        }

        private void BtnPlayRollType_OnClick(object sender, RoutedEventArgs e)
        {
            if (Common.GLOBAL["PERSONALFM"].ToString() != "true")
            {
                switch (NowPlayType)
                {
                    case PlayMode.DefaultRoll:
                        //变成随机
                        HyPlayList.NowPlayType = PlayMode.Shuffled;
                        NowPlayType = PlayMode.Shuffled;
                        IconPlayType.Glyph = "\uE14B";
                        break;
                    case PlayMode.Shuffled:
                        //变成单曲
                        IconPlayType.Glyph = "\uE1CC";
                        HyPlayList.NowPlayType = PlayMode.SinglePlay;
                        NowPlayType = PlayMode.SinglePlay;
                        break;
                    case PlayMode.SinglePlay:
                        //变成顺序
                        HyPlayList.NowPlayType = PlayMode.DefaultRoll;
                        NowPlayType = PlayMode.DefaultRoll;
                        IconPlayType.Glyph = "\uE169";
                        break;
                }
            }
            else
            {
                _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.FmTrash,
                    new Dictionary<string, object> { { "id", HyPlayList.NowPlayingItem.PlayItem.id } });
                PersonalFM.LoadNextFM();
            }
        }

        private void BtnLike_OnClick(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Netease)
            {
                Api.LikeSong(HyPlayList.NowPlayingItem.PlayItem.id,
                    !Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.id));
                if (Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.id))
                    Common.LikedSongs.Remove(HyPlayList.NowPlayingItem.PlayItem.id);
                else
                    Common.LikedSongs.Add(HyPlayList.NowPlayingItem.PlayItem.id);

                BtnLike.IsChecked = Common.LikedSongs.Contains(HyPlayList.NowPlayingItem.PlayItem.id);
            }
            else if (HyPlayList.NowPlayingItem.ItemType == HyPlayItemType.Radio)
            {
                _ = Common.ncapi.RequestAsync(CloudMusicApiProviders.ResourceLike,
                    new Dictionary<string, object>
                        {{"type", "4"}, {"t", "1"}, {"id", HyPlayList.NowPlayingItem.PlayItem.id}});
            }
            else
            {
                BtnLike.IsChecked = false;
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

                    //ButtonCollapse_OnClick(this, null);
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
                await new SongListSelect(HyPlayList.NowPlayingItem.PlayItem.id).ShowAsync();
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
                Common.NavigatePage(typeof(Comments), "sg" + HyPlayList.NowPlayingItem.PlayItem.id);
            else
                Common.NavigatePage(typeof(Comments), "fm" + HyPlayList.NowPlayingItem.PlayItem.Album.alias);
            Common.NavigatePage(typeof(BlankPage));
            ButtonCollapse_OnClick(this, e);
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
                                               HyPlayList.NowPlayingItem.PlayItem.id));
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

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            InitializeDesktopLyric();
        }

        private void BtnPlayStateChange_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            BtnPlayStateChange_OnClick(sender, e);
        }

        private void ImageContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ButtonExpand_OnClick(sender, e);
        }


    }


    public class ListViewPlayItem
    {
        public ListViewPlayItem(string name, int index, string artist)
        {
            Name = name;
            Artist = artist;
            this.index = index;
        }

        public string Name { get; }
        public string Artist { get; }
        public string DisplayName => Artist + " - " + Name;

        public int index { get; }

        public override string ToString()
        {
            return Artist + " - " + Name;
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

}