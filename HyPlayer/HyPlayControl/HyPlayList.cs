#region

using ALRC.Converters;
using AudioEffectComponent;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using Kawazu;
using LyricParser.Abstraction;
using LyricParser.Implementation;
using Microsoft.Toolkit.Uwp.Helpers;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Timers;
using Windows.Devices.Enumeration;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.Xaml.Media;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using Buffer = Windows.Storage.Streams.Buffer;
using File = TagLib.File;
using LrcConverter = ALRC.Converters.LrcConverter;

#endregion

namespace HyPlayer.HyPlayControl;

public static class HyPlayList
{
    public delegate void LoginDoneEvent();

    public delegate void LyricChangeEvent();

    public delegate void LyricColorChangeEvent();

    public delegate void LyricLoadedEvent();

    public delegate void MediaEndEvent(HyPlayItem hpi);

    //public delegate void PlayItemAddEvent(HyPlayItem playItem);
    //public static event PlayItemAddEvent OnPlayItemAdd; //此方法因为效率原因废弃
    public delegate void PauseEvent();

    public delegate void PlayEvent();


    /********        事件        ********/
    public delegate void PlayItemChangeEvent(HyPlayItem playItem);

    public delegate void PlayListAddDoneEvent();

    public delegate void PlayPositionChangeEvent(TimeSpan position);

    public delegate void SongBufferEndEvent();

    public delegate void SongBufferStartEvent();

    public delegate void SongMoveNextEvent();

    public delegate void SongRemoveAllEvent();

    public delegate void TimerTicked();

    public delegate void VolumeChangeEvent(double newVolume);

    public delegate void SongLikeStatusChanged(bool isLiked);

    public delegate Task SongCoverChanged(int hashCode, IBuffer coverStream);

    public static int NowPlaying;
    private static readonly System.Timers.Timer SecTimer = new(1000); // 公用秒表
    public static readonly List<HyPlayItem> List = new();
    public static readonly List<int> ShuffleList = new();
    public static int ShufflingIndex = -1;
    public static LyricInfo LyricInfo = new();
    public static TimeSpan LyricOffset = TimeSpan.Zero;
    public static PropertySet AudioEffectsProperties = new PropertySet();

    /********        API        ********/
    public static MediaPlayer Player;
    public static SystemMediaTransportControls MediaSystemControls;
    private static SystemMediaTransportControlsDisplayUpdater _controlsDisplayUpdater;
    private static readonly BackgroundDownloader Downloader = new();
    private static Dictionary<HyPlayItem, DownloadOperation> DownloadOperations = new();
    public static InMemoryRandomAccessStream CoverStream = new InMemoryRandomAccessStream();
    public static IBuffer CoverBuffer;
    public static RandomAccessStreamReference CoverStreamReference =
        RandomAccessStreamReference.CreateFromStream(CoverStream);

    public static int NowPlayingHashCode = 0;
    private static InMemoryRandomAccessStream _ncmPlayableStream = new();
    private static string _ncmPlayableStreamMIMEType = string.Empty;
    private static MediaSource _mediaSource;
    private static Task _playerLoaderTask;
    private static HyPlayItem _requestedItem;
    private static int _songIsWaitingForLoadCount = 0;

    public static int LyricPos;

    public static string PlaySourceId;
    private static double _playerOutgoingVolume;

    //Fade
    private static DateTime FadeStartTime;
    public static bool AutoFadeProcessing;
    private static double FadeLastVolume = 1;
    private static double FadeVolume = 1;
    public static double AdvFadeVolume = 1;
    public static bool FadeProcessStatus = false;
    public static bool AdvFadeProcessStatus = false;
    public static bool UserRequestedChangingSong = false;
    public static FadeInOutState CurrentFadeInOutState;
    private static bool OnlyFadeOutVolume = false;

    public enum FadeInOutState
    {
        FadeIn = 0,
        FadeOut = 1
    };

    public enum SongChangeType
    {
        Previous = 0,
        Next = 1,
        None = -1
    }

    public enum SongFadeEffectType
    {
        PauseFadeOut = 1,
        PlayFadeIn = 2,
        AutoNextFadeOut = 3,
        UserNextFadeOut = 4,
        NextFadeIn = 5,
        AdvFadeOut = 6
    }

    private static bool FadeReveserd = false;
    public static bool FadeLocked = false;
    private static double FadeTime;

    public static double PlayerOutgoingVolume
    {
        get => _playerOutgoingVolume;
        set
        {
            _playerOutgoingVolume = value;
            Common.Setting.Volume = (int)(value * 100);
            OnVolumeChange?.Invoke(_playerOutgoingVolume);
            VolumeChangeProcess();
        }
    }
    public static bool LockSeeking = false;
    public static bool PlaybackErrorHandling = false;

    /*********        基本       ********/
    public static PlayMode NowPlayType
    {
        set
        {
            Common.Setting.songRollType = (int)value;
            // 新版随机创建随机表
            if (value == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
                CreateShufflePlayLists();
            if (value != PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
                OnPlayListAddDone?.Invoke();
        }

        get => (PlayMode)Common.Setting.songRollType;
    }

    public static bool IsPlaying => Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public static StorageFile NowPlayingStorageFile { get; private set; }


    public static HyPlayItem NowPlayingItem
    {
        get
        {
            if (_mediaSource != null && _mediaSource.IsOpen)
            {
                return _mediaSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
            }

            if (List.Count <= NowPlaying || NowPlaying == -1)
                return new HyPlayItem { ItemType = HyPlayItemType.Netease };
            return List[NowPlaying];
        }
    }

    public static event PlayItemChangeEvent OnPlayItemChange;

    public static event PauseEvent OnPause;

    public static event PlayEvent OnPlay;

    public static event PlayPositionChangeEvent OnPlayPositionChange;

    public static event VolumeChangeEvent OnVolumeChange;

    public static event PlayListAddDoneEvent OnPlayListAddDone;

    public static event LyricLoadedEvent OnLyricLoaded;

    public static event LyricChangeEvent OnLyricChange;
    public static event LyricColorChangeEvent OnLyricColorChange;

    public static event MediaEndEvent OnMediaEnd;

    public static event LyricChangeEvent OnSongMoveNext;

    public static event LoginDoneEvent OnLoginDone;

    public static event TimerTicked OnTimerTicked;

    public static event SongRemoveAllEvent OnSongRemoveAll;

    public static event SongLikeStatusChanged OnSongLikeStatusChange;

    public static event SongCoverChanged OnSongCoverChanged;

    public static void InitializeHyPlaylist()
    {
        Player = new MediaPlayer
        {
            AutoPlay = true,
            IsLoopingEnabled = false
        };
        MediaSystemControls = SystemMediaTransportControls.GetForCurrentView();
        _controlsDisplayUpdater = MediaSystemControls.DisplayUpdater;
        Player.CommandManager.IsEnabled = Common.Setting.ancientSMTC;
        MediaSystemControls.IsPlayEnabled = true;
        MediaSystemControls.IsPauseEnabled = true;
        MediaSystemControls.IsNextEnabled = true;
        MediaSystemControls.IsPreviousEnabled = true;
        MediaSystemControls.IsEnabled = true;
        MediaSystemControls.ButtonPressed += SystemControls_ButtonPressed;
        MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
        Player.MediaEnded += Player_MediaEnded;
        Player.CurrentStateChanged += Player_CurrentStateChanged;
        //Player.VolumeChanged += Player_VolumeChanged;
        Player.PlaybackSession.PositionChanged += PlaybackSession_PositionChanged;
        if (Common.Setting.progressInSMTC)
        {
            MediaSystemControls.PlaybackPositionChangeRequested += MediaSystemControls_PlaybackPositionChangeRequested;
            Player.PlaybackSession.PositionChanged += UpdateSmtcPosition;
        }

        Player.MediaFailed += PlayerOnMediaFailed;
        Player.SourceChanged += Player_SourceChanged;
        SecTimer.Elapsed += (sender, args) => _ = Common.Invoke(() => OnTimerTicked?.Invoke());
        SecTimer.Start();
        if (Common.Setting.highPreciseLyricTimer)
        {
            highTimer.Elapsed += (_, _) => { LoadLyricChange(); };
            highTimer.Start();
        }
        HistoryManagement.InitializeHistoryTrack();
        if (!Common.Setting.EnableAudioGain) AudioEffectsProperties["AudioGain_Disabled"] = true;
        Player.AddAudioEffect(typeof(AudioGainEffect).FullName, true, AudioEffectsProperties);
        Common.IsInFm = false;
    }

    public static void Seek(TimeSpan targetTimeSpan)
    {
        if (LockSeeking) return;
        Player.PlaybackSession.Position = targetTimeSpan;
    }
    public static void FireLyricColorChangeEvent()
    {
        OnLyricColorChange?.Invoke();
    }

    public static void UpdateSmtcPosition(MediaPlaybackSession sender, object args)
    {
        MediaSystemControls.PlaybackRate = Player.PlaybackSession.PlaybackRate;
        MediaSystemControls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            Position = Player.PlaybackSession.Position,
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = Player.PlaybackSession.NaturalDuration,
            EndTime = Player.PlaybackSession.NaturalDuration
        });
    }

    public static void MediaSystemControls_PlaybackPositionChangeRequested(SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        Seek(args.RequestedPlaybackPosition);
    }


    public static void LoginDoneCall()
    {
        _ = Common.Invoke(() => { OnLoginDone?.Invoke(); });
    }


    private static void PlayerOnMediaFailed(string reason)
    {
        //歌曲崩溃了的话就是这个
        //SongMoveNext();

        Common.ErrorMessageList.Add($"歌曲播放失败: {NowPlayingItem.PlayItem.Name}\n{reason}");
        Common.AddToTeachingTipLists($"播放失败 切到下一曲 \n 歌曲: {NowPlayingItem.PlayItem.Name}\n{reason}");
        SongMoveNext();
    }
    private static async void PlayerOnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if ((uint)args.ExtendedErrorCode.HResult == 0xC00D36FA)
        {
            Common.AddToTeachingTipLists("播放失败", "无法创建媒体接收器，请检查设备是否有声音输出设备！");
            return;
        }
        if ((uint)args.ExtendedErrorCode.HResult == 0x80004004
            || (uint)args.ExtendedErrorCode.HResult == 0xC00D36BB
            || (uint)args.ExtendedErrorCode.HResult == 0x80004005)
        {
            if (PlaybackErrorHandling) return;
            PlaybackErrorHandling = true;
            Common.AddToTeachingTipLists("播放错误", "操作过快，请稍候...");
            LockSeeking = true;
            await Task.Delay(1000);
            var position = Player.PlaybackSession.Position;
            Player.AutoPlay = false;
            Player.Source = _mediaSource;
            Player.PlaybackSession.Position = position;
            Player.Play();
            Player.AutoPlay = true;
            LockSeeking = false;
            PlaybackErrorHandling = false;
            return;
        }
        Common.ErrorMessageList.Add($"歌曲播放失败: {NowPlayingItem.PlayItem.Name}\n{args.ErrorMessage}\n{args.ExtendedErrorCode}");
        Common.AddToTeachingTipLists($"播放失败 切到下一曲 \n 歌曲: {NowPlayingItem.PlayItem.Name}\n{args.ErrorMessage}\n{args.ExtendedErrorCode}");
        SongMoveNext();
    }

    public static async Task PickLocalFile()
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".flac");
        fop.FileTypeFilter.Add(".mp3");
        fop.FileTypeFilter.Add(".ncm");
        fop.FileTypeFilter.Add(".ape");
        fop.FileTypeFilter.Add(".m4a");
        fop.FileTypeFilter.Add(".wav");


        var files =
            await fop.PickMultipleFilesAsync();
        //HyPlayList.RemoveAllSong();
        var isFirstLoad = true;
        foreach (var file in files)
        {
            var folder = await file.GetParentAsync();
            if (folder != null)
            {
                if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(folder.Path.GetHashCode().ToString()))
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace(folder.Path.GetHashCode().ToString(),
                        folder);
            }
            else
            {
                if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(file.Path.GetHashCode().ToString()))
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace(file.Path.GetHashCode().ToString(),
                        file);
            }

            if (Path.GetExtension(file.Path) == ".ncm")
            {
                //脑残Music
                using var stream = await file.OpenStreamForReadAsync();
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
                            Url = file.Path,
                            SubExt = Info.format,
                            Bitrate = Info.bitrate,
                            IsLocalFile = true,
                            Type = HyPlayItemType.Netease,
                            LengthInMilliseconds = Info.duration,
                            Id = Info.musicId.ToString(),
                            TrackId = -1,
                            CDName = "01",
                            Artist = null,
                            /*
                            size = sf.GetBasicPropertiesAsync()
                                .GetAwaiter()
                                .GetResult()
                                .Size.ToString(),
                            */
                            Name = Info.musicName,
                            InfoTag = file.Provider.DisplayName + " NCM"
                        }
                    };
                    hyitem.PlayItem.Artist = Info.artist.Select(t => new NCArtist
                    { name = t[0].ToString(), id = t[1].ToString() })
                        .ToList();

                    List.Add(hyitem);
                }
            }
            else
            {
                await AppendStorageFile(file);
            }

            if (!isFirstLoad) continue;
            isFirstLoad = false;
        }

        //HyPlayList.SongMoveTo(0);
        SongAppendDone();
        SongMoveTo(List.Count - 1);
    }


    private static async Task LoadLocalFile(HyPlayItem targetItem)
    {
        // 此处可以改进
        if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm") throw new ArgumentException();
        if (targetItem.PlayItem.DontSetLocalStorageFile != null)
        {
            if (targetItem.ItemType != HyPlayItemType.LocalProgressive)
            {
                NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
            }
            else
            {
                NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
                var item = await LoadStorageFile(targetItem.PlayItem.DontSetLocalStorageFile);
                targetItem.ItemType = HyPlayItemType.Local;
                targetItem.PlayItem = item.PlayItem;
                targetItem.PlayItem.DontSetLocalStorageFile = NowPlayingStorageFile;
            }
        }
        else
        {
            NowPlayingStorageFile = await StorageFile.GetFileFromPathAsync(targetItem.PlayItem.Url);
        }


        //Player_SourceChanged(null, null);
    }

    public async static Task LoadNCMFile(HyPlayItem targetItem)
    {
        // 脑残Music解析
        using var stream = await targetItem.PlayItem.DontSetLocalStorageFile.OpenStreamForReadAsync();
        if (NCMFile.IsCorrectNCMFile(stream))
        {
            var info = NCMFile.GetNCMMusicInfo(stream);
            var coverArray = NCMFile.GetCoverByteArray(stream);
            var buffer = coverArray.AsBuffer();
            await CoverStream.WriteAsync(buffer);
            using var encStream = NCMFile.GetEncryptedStream(stream);
            encStream.Seek(0, SeekOrigin.Begin);
            var songDataStream = new InMemoryRandomAccessStream();
            var targetSongDataStream = songDataStream.AsStream();
            encStream.CopyTo(targetSongDataStream);
            _ncmPlayableStream = songDataStream;
            NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
            _ncmPlayableStreamMIMEType = MIMEHelper.GetNCMFileMimeType(info.format);
        }
    }

    /********        方法         ********/
    public static void SongAppendDone(string currentSongId = "-1")
    {
        Common.IsInFm = false;
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
        {
            CreateShufflePlayLists(currentSongId);
        }
        else
            _ = Common.Invoke(() => OnPlayListAddDone?.Invoke());
    }

    public static void SongMoveNext()
    {
        OnSongMoveNext?.Invoke();
        if (List.Count == 0) return;
        MoveSongPointer(true);
        if (List.Count != 0)
        {
            _ = LoadPlayerSong(List[NowPlaying]);
        }
    }

    public static void SongMovePrevious()
    {
        if (List.Count == 0) return;
        if (NowPlaying - 1 < 0)
            NowPlaying = List.Count - 1;
        else
            NowPlaying--;
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
        {
            // 新版随机上一曲
            if (--ShufflingIndex < 0)
                ShufflingIndex = ShuffleList.Count - 1;
            NowPlaying = ShuffleList[ShufflingIndex];
        }

        if (!Common.IsInFm && List.Count != 0)
        {
            _ = LoadPlayerSong(List[NowPlaying]);
        }
    }

    public static void SongMoveTo(int index)
    {
        if (List.Count <= index) return;
        NowPlaying = index;
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
            ShufflingIndex = ShuffleList.IndexOf(index);
        _ = LoadPlayerSong(List[NowPlaying]);
    }

    public static void RemoveSong(int index)
    {
        if (List.Count <= index) return;
        if (List.Count - 1 == 0)
        {
            RemoveAllSong();
            return;
        }

        if (index == NowPlaying)
        {
            List.RemoveAt(index);
            _ = LoadPlayerSong(List[NowPlaying]);
        }

        if (index < NowPlaying)
        {
            //需要将序号向前挪动
            NowPlaying--;
            List.RemoveAt(index);
        }

        if (index > NowPlaying)
            List.RemoveAt(index);
        //假如移除后面的我就不管了
        SongAppendDone();
    }

    public static void ManualRemoveAllSong()
    {
        RemoveAllSong();
        NotifyPlayItemChanged(NowPlayingItem);
    }

    public static void RemoveAllSong(bool resetPlaying = true)
    {
        if (List.Count == 0) return;
        List.Clear();
        if (resetPlaying)
            Player.Source = null;
        NowPlaying = -1;
        OnSongRemoveAll?.Invoke();
        SongAppendDone();
    }

    public static async void LikeSong()
    {
        var isLiked = Common.LikedSongs.Contains(NowPlayingItem.PlayItem.Id);
        switch (NowPlayingItem.ItemType)
        {
            case HyPlayItemType.Netease:
                {
                    bool res = await Api.LikeSong(NowPlayingItem.PlayItem.Id,
                        !isLiked);
                    if (res)
                    {
                        if (isLiked)
                            Common.LikedSongs.Remove(NowPlayingItem.PlayItem.Id);
                        else
                            Common.LikedSongs.Add(NowPlayingItem.PlayItem.Id);
                        OnSongLikeStatusChange?.Invoke(!isLiked);
                    }
                    break;
                }
            case HyPlayItemType.Radio:
                // TODO: 待实现电台红心
                Common.AddToTeachingTipLists("暂不支持红心电台歌曲", "将在后续版本中支持");
                OnSongLikeStatusChange?.Invoke(!isLiked);
                break;
        }
    }
    /********        相关事件处理        ********/

    private static async void SystemControls_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                //Player.Play();
                await SongFadeRequest(SongFadeEffectType.PlayFadeIn);
                break;
            case SystemMediaTransportControlsButton.Pause:
                //Player.Pause();
                await SongFadeRequest(SongFadeEffectType.PauseFadeOut);
                break;
            case SystemMediaTransportControlsButton.Previous:
                //SongMovePrevious();
                await SongFadeRequest(SongFadeEffectType.UserNextFadeOut, SongChangeType.Previous);
                break;
            case SystemMediaTransportControlsButton.Next:
                //SongMoveNext();
                await SongFadeRequest(SongFadeEffectType.UserNextFadeOut, SongChangeType.Next);
                break;
        }
    }

    private static void MoveSongPointer(bool realNext = false)
    {
        //首先切换指针到下一首要播放的歌
        switch (NowPlayType)
        {
            case PlayMode.DefaultRoll:
                //正常Roll的话,id++
                if (NowPlaying + 1 >= List.Count)
                    NowPlaying = 0;
                else
                    NowPlaying++;

                break;
            case PlayMode.Shuffled:
                // 随机播放
                if (Common.Setting.shuffleNoRepeating)
                {
                    // 新版乱序算法
                    if (++ShufflingIndex > List.Count - 1)
                        ShufflingIndex = 0;
                    NowPlaying = ShuffleList[ShufflingIndex];
                }
                else
                {
                    NowPlaying = new Random(DateTime.Now.Millisecond).Next(List.Count - 1);
                }

                break;
            case PlayMode.SinglePlay:
                if (realNext)
                {
                    if (NowPlaying + 1 >= List.Count)
                        NowPlaying = 0;
                    else
                        NowPlaying++;
                }

                break;
        }
    }

    private static void Player_MediaEnded(MediaPlayer sender, object args)
    {
        //当播放结束时,此时你应当进行切歌操作
        //不过在此之前还是把订阅了的时间给返回回去吧
        OnMediaEnd?.Invoke(NowPlayingItem);
        MoveSongPointer();
        //然后尝试加载下一首歌
        if (List.Count != 0)
        {
            _ = LoadPlayerSong(List[NowPlaying]);
        }
    }

    public static async Task AdvFadeProcess()
    {
        var fadeNextTime = TimeSpan.FromSeconds(Common.Setting.fadeNextTime);
        while (AdvFadeProcessStatus)
        {
            AdvFadeVolume = 1 - TimeRangeToVolumeRangeConverter(currentTime: Player.PlaybackSession.Position,
                fadeStartTime: Player.PlaybackSession.NaturalDuration - fadeNextTime,
                fadeEndTime: Player.PlaybackSession.NaturalDuration, miniumVolume: 0, maxiumVolume: 1);
            if (AdvFadeVolume < 0)
            {
                AdvFadeVolume = 0;
                AdvFadeProcessStatus = false;
            }

            if (AdvFadeVolume > 1)
            {
                AdvFadeVolume = 1;
                AdvFadeProcessStatus = false;
            }

            VolumeChangeProcess();
            await Task.Delay(10);
        }
    }

    private static async Task FadeProcess()
    {
        FadeStartTime = DateTime.Now;
        FadeProcessStatus = true;
        if (CurrentFadeInOutState == FadeInOutState.FadeIn)
        {
            Player.Play();
        }

        while (FadeProcessStatus)
        {
            if (CurrentFadeInOutState == FadeInOutState.FadeIn)
            {
                if (FadeReveserd)
                {
                    FadeVolume = TimeRangeToVolumeRangeConverter(currentTime: DateTime.Now,
                        fadeStartTime: FadeStartTime, fadeEndTime: FadeStartTime.AddSeconds(FadeTime),
                        miniumVolume: FadeLastVolume, maxiumVolume: 1);
                }
                else
                {
                    FadeVolume = TimeRangeToVolumeRangeConverter(currentTime: DateTime.Now,
                        fadeStartTime: FadeStartTime, fadeEndTime: FadeStartTime.AddSeconds(FadeTime), miniumVolume: 0,
                        maxiumVolume: 1);
                }

                if (FadeTime == 0 || FadeVolume > 1)
                {
                    FadeVolume = 1;
                    FadeProcessStatus = false;
                    FadeReveserd = false;
                    FadeLocked = false;
                    AutoFadeProcessing = false;
                }
            }
            else
            {
                if (FadeReveserd)
                {
                    FadeVolume = 1 - TimeRangeToVolumeRangeConverter(currentTime: DateTime.Now,
                        fadeStartTime: FadeStartTime, fadeEndTime: FadeStartTime.AddSeconds(FadeTime),
                        miniumVolume: 1 - FadeLastVolume, maxiumVolume: 1);
                }
                else
                {
                    FadeVolume = 1 - TimeRangeToVolumeRangeConverter(currentTime: DateTime.Now,
                        fadeStartTime: FadeStartTime, fadeEndTime: FadeStartTime.AddSeconds(FadeTime), miniumVolume: 0,
                        maxiumVolume: 1);
                }

                if (FadeTime == 0 || FadeVolume < 0)
                {
                    FadeVolume = 0;
                    FadeProcessStatus = false;
                    FadeReveserd = false;
                    FadeLocked = false;
                    AutoFadeProcessing = false;
                    if (!OnlyFadeOutVolume)
                    {
                        Player.Pause();
                    }
                }
            }

            VolumeChangeProcess();
            await Task.Delay(10);
        }
    }

    private static void FadeProcessingChanged()
    {
        FadeStartTime = DateTime.Now;
        FadeLastVolume = FadeVolume;
        if (CurrentFadeInOutState == FadeInOutState.FadeIn)
        {
            CurrentFadeInOutState = FadeInOutState.FadeOut;
        }
        else
        {
            CurrentFadeInOutState = FadeInOutState.FadeIn;
        }

        FadeReveserd = true;
    }


    private static void FindChancetoMoveSong(SongChangeType songChangeType)
    {
        while (UserRequestedChangingSong)
        {
#if DEBUG
            Debug.WriteLine("FindStart");
#endif
            CurrentFadeInOutState = FadeInOutState.FadeOut;
            if (FadeVolume == 0 || Player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
            {
                if (songChangeType == SongChangeType.Next)
                {
                    SongMoveNext();
                }
                else
                {
                    SongMovePrevious();
                }
#if DEBUG
                Debug.WriteLine("FindEnd");
#endif
                UserRequestedChangingSong = false;
            }

            if (CurrentFadeInOutState == FadeInOutState.FadeIn)
            {
#if DEBUG
                Debug.WriteLine("Break");
#endif
                UserRequestedChangingSong = false;
            }
        }
    }

    private static double TimeRangeToVolumeRangeConverter(DateTime currentTime, DateTime fadeStartTime,
        DateTime fadeEndTime, double miniumVolume, double maxiumVolume)
    {
        double resultVolume;
        var fadeTimeRange = fadeEndTime - fadeStartTime;
        var volumeRange = maxiumVolume - miniumVolume;
        if (fadeTimeRange <= TimeSpan.Zero)
        {
            resultVolume = maxiumVolume;
        }
        else
        {
            resultVolume = ((currentTime - fadeStartTime) * volumeRange / fadeTimeRange) + miniumVolume;
        }

        return resultVolume;
    }

    private static double TimeRangeToVolumeRangeConverter(TimeSpan currentTime, TimeSpan fadeStartTime,
        TimeSpan fadeEndTime, double miniumVolume, double maxiumVolume)
    {
        double resultVolume;
        var fadeTimeRange = fadeEndTime - fadeStartTime;
        var volumeRange = maxiumVolume - miniumVolume;
        if (fadeTimeRange <= TimeSpan.Zero)
        {
            resultVolume = maxiumVolume;
        }
        else
        {
            resultVolume = ((currentTime - fadeStartTime) * volumeRange / fadeTimeRange) + miniumVolume;
        }

        return resultVolume;
    }

    public static void VolumeChangeProcess()
    {
        Player.Volume = FadeVolume * AdvFadeVolume * _playerOutgoingVolume;
#if DEBUG
        Debug.WriteLine(FadeVolume);
        Debug.WriteLine(AdvFadeVolume);
#endif
    }

    public static async Task SongFadeRequest(SongFadeEffectType requestedFadeType,
        SongChangeType songChangeType = SongChangeType.Next)
    {
        if (!FadeLocked)
        {
            switch (requestedFadeType)
            {
                case SongFadeEffectType.PauseFadeOut:
                    OnlyFadeOutVolume = false;
                    FadeTime = Common.Setting.fadePauseTime;
                    if (!FadeProcessStatus)
                    {
                        CurrentFadeInOutState = FadeInOutState.FadeOut;
                        await FadeProcess();
                    }
                    else
                    {
                        FadeProcessingChanged();
                    }

                    break;
                case SongFadeEffectType.PlayFadeIn:
                    OnlyFadeOutVolume = false;
                    FadeTime = Common.Setting.fadePauseTime;
                    if (!FadeProcessStatus)
                    {
                        CurrentFadeInOutState = FadeInOutState.FadeIn;
                        await FadeProcess();
                    }
                    else
                    {
                        FadeProcessingChanged();
                    }

                    break;
                case SongFadeEffectType.AutoNextFadeOut:
                    OnlyFadeOutVolume = true;
                    AutoFadeProcessing = true;
                    FadeLocked = true;
                    FadeTime = Common.Setting.fadeNextTime;
                    if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused || !(Common.Setting.fadeNext))
                    {
                        FadeTime = 0;
                    }

                    if (!FadeProcessStatus)
                    {
                        CurrentFadeInOutState = FadeInOutState.FadeOut;
                        await FadeProcess();
                    }
                    else
                    {
                        FadeStartTime = DateTime.Now;
                        FadeLastVolume = FadeVolume;
                        CurrentFadeInOutState = FadeInOutState.FadeOut;
                        FadeReveserd = true;
                    }

                    break;
                case SongFadeEffectType.UserNextFadeOut:
                    if (Common.Setting.disableFadeWhenChangingSongManually)
                    {
                        if (songChangeType == SongChangeType.Next)
                        {
                            SongMoveNext();
                        }
                        else
                        {
                            SongMovePrevious();
                        }

                        return;
                    }

                    OnlyFadeOutVolume = false;
                    FadeLocked = true;
                    FadeTime = Common.Setting.fadeNextTime;
                    if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused || !(Common.Setting.fadeNext))
                    {
                        FadeTime = 0;
                    }

                    if (!FadeProcessStatus)
                    {
                        CurrentFadeInOutState = FadeInOutState.FadeOut;
                        await FadeProcess();
                        if (FadeVolume == 0)
                        {
                            if (songChangeType == SongChangeType.Next)
                            {
                                SongMoveNext();
                            }
                            else
                            {
                                SongMovePrevious();
                            }
                        }
                    }
                    else
                    {
                        FadeProcessingChanged();
                        FadeLocked = true;
                        if (!UserRequestedChangingSong)
                        {
                            UserRequestedChangingSong = true;
                            FindChancetoMoveSong(songChangeType);
                        }
                        else
                        {
                            UserRequestedChangingSong = false;
                        }
                    }

                    break;
            }
        }

        switch (requestedFadeType)
        {
            case SongFadeEffectType.NextFadeIn:
                AutoFadeProcessing = false;
                OnlyFadeOutVolume = false;
                FadeVolume = 0;
                CurrentFadeInOutState = FadeInOutState.FadeIn;
                FadeStartTime = DateTime.Now;
                FadeReveserd = false;
                FadeTime = Common.Setting.fadeNextTime;
                Player.Play();
                AutoFadeProcessing = false;
                AdvFadeVolume = 1;
                AdvFadeProcessStatus = false;
                VolumeChangeProcess();
                FadeLocked = false;
                if (!FadeProcessStatus)
                {
                    CurrentFadeInOutState = FadeInOutState.FadeIn;
                    await FadeProcess();
                }

                break;
            case SongFadeEffectType.AdvFadeOut:
                AutoFadeProcessing = true;
                if (!AdvFadeProcessStatus)
                {
                    AdvFadeProcessStatus = true;
                    await AdvFadeProcess();
                }

                break;
        }
    }


    private static async Task<string> GetNowPlayingUrl(HyPlayItem targetItem)
    {
        var playUrl = targetItem.PlayItem.Url;
        // 对了,先看看是否要刷新播放链接
        if ((string.IsNullOrEmpty(targetItem.PlayItem.Url) ||
            Common.Setting.songUrlLazyGet) && targetItem.PlayItem.Id != "-1")
            try
            {
                var songRequest = new SongUrlRequest { Level = Common.Setting.audioRate, Id = targetItem.PlayItem.Id };
                var songResult = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongUrlApi, songRequest);
                if (songResult.IsSuccess)
                {
                    if (songResult.Value.SongUrls[0].Code == 200)
                    {
                        if (!string.IsNullOrEmpty(songResult.Value.SongUrls[0].FreeTrialInfo) && Common.Setting.jumpVipSongPlaying)
                        {
                            throw new Exception("当前歌曲为 VIP 试听, 已自动跳过");
                        }

                        playUrl = songResult.Value.SongUrls[0].Url;
                        if (Common.Setting.UseHttpWhenGettingSongs && playUrl.Contains("https://"))
                        {
                            playUrl = playUrl.Replace("https://", "http://");
                        }


                        var tag = songResult.Value.SongUrls[0]?.Level
                            switch
                        {
                            "standard" => "标准",
                            "higher" => "较高",
                            "exhigh" => "极高",
                            "lossless" => "无损",
                            "hires" => "Hi-Res",
                            "jyeffect" => "高清环绕声",
                            "sky" => "沉浸环绕声",
                            "jymaster" => "超清母带",
                            _ => "在线"
                        };
                        targetItem.PlayItem.QualityTag = tag;


                        AudioEffectsProperties["AudioGain_GainValue"] = songResult.Value.SongUrls[0]?.Gain ?? 0f;
                        _ = Common.Invoke(() =>
                        {
                            Common.BarPlayBar.TbSongTag.Text = targetItem.PlayItem.QualityTag;
                            if (targetItem.PlayItem.QualityTag.Length > 2)
                            {
                                var backgroundbrush = new LinearGradientBrush();
                                backgroundbrush.StartPoint = new Windows.Foundation.Point(0, 0);
                                backgroundbrush.EndPoint = new Windows.Foundation.Point(1, 1);

                                backgroundbrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(255, 251, 251, 206) });
                                backgroundbrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(255, 223, 155, 28) });

                                Common.BarPlayBar.SongInfoTag.Background = backgroundbrush;
                                Common.BarPlayBar.SongInfoTag.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                                Common.BarPlayBar.TbSongTag.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                            }
                            else
                            {
                                var brush = new SolidColorBrush(Colors.Red);
                                Common.BarPlayBar.SongInfoTag.BorderBrush = brush;
                                Common.BarPlayBar.SongInfoTag.Background = null;
                                Common.BarPlayBar.TbSongTag.Foreground = brush;
                            }
                        });
                    }
                }
                else
                {
                    throw new Exception("下载链接获取失败"); //传一个播放失败
                }
            }
            catch
            {
                throw new Exception("下载链接获取失败"); //传一个播放失败
            }

        return playUrl;
    }

    public static async Task LoadPlayerSong(HyPlayItem targetItem)
    {
        _requestedItem = targetItem;
        if (_songIsWaitingForLoadCount > 1)
        {
            return;
        }

        if (_playerLoaderTask != null && !_playerLoaderTask.IsCompleted)
        {
            _songIsWaitingForLoadCount++;
            if (_songIsWaitingForLoadCount <= 1)
            {
                await _playerLoaderTask;
                _songIsWaitingForLoadCount--;
            }
            else
            {
                _songIsWaitingForLoadCount--;
                return;
            }
        }

        if (_requestedItem != null)
        {
            _playerLoaderTask = LoadMediaSource(_requestedItem);
            _requestedItem = null;
        }
    }

    public static async Task LoadMediaSource(HyPlayItem targetItem)
    {
        if (targetItem.PlayItem?.Name == null)
        {
            MoveSongPointer();
            return;
        }
        NowPlayingHashCode = 0;
        if (CoverStream.Size != 0)
        {
            CoverStream.Size = 0;
            CoverStream.Seek(0);
        }

        if (_ncmPlayableStream != null && _ncmPlayableStream.Size != 0)
        {
            _ncmPlayableStream.Dispose();
            _ncmPlayableStream = null;
        }

        if (_ncmPlayableStreamMIMEType != string.Empty)
        {
            _ncmPlayableStreamMIMEType = string.Empty;
        }

        try
        {
            Player.Source = null;
            _mediaSource?.Dispose();
            switch (targetItem.ItemType)
            {
                case HyPlayItemType.Netease:
                case HyPlayItemType.Radio: //FM伪加载为普通歌曲
                    //先看看是不是本地文件
                    //本地文件的话尝试加载
                    //cnm的NCM,我试试其他方式
                    if (targetItem.PlayItem.IsLocalFile)
                    {
                        if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
                        {
                            await LoadNCMFile(targetItem);
                            _mediaSource = MediaSource.CreateFromStream(_ncmPlayableStream, _ncmPlayableStreamMIMEType);
                        }
                        else
                        {
                            await LoadLocalFile(targetItem);
                            _mediaSource = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
                        }
                    }
                    else
                    {
                        if (Common.Setting.enableCache)
                        {
                            //再检测是否已经缓存且大小正常
                            try
                            {
                                // 加载本地缓存文件
                                var sf =
                                    await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir))
                                        .GetFileAsync(targetItem.PlayItem.Id +
                                                      ".cache");
                                if ((await sf.GetBasicPropertiesAsync()).Size.ToString() ==
                                    targetItem.PlayItem.Size || targetItem.PlayItem.Size == null)
                                {
                                    _mediaSource = MediaSource.CreateFromStorageFile(sf);
                                }

                                else
                                {
                                    await sf.DeleteAsync();
                                    throw new Exception("File Size Not Match");
                                }
                            }
                            catch
                            {
                                try
                                {
                                    var playUrl = await GetNowPlayingUrl(targetItem);
                                    IStorageFile resultFile = null;
                                    //尝试从DownloadOperation下载
                                    if (playUrl != null)
                                    {
                                        var destinationFolder =
                                                await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir);

                                        if (!DownloadOperations.ContainsKey(targetItem))
                                        {
                                            var destinationFile =
                                                await destinationFolder.CreateFileAsync(
                                                    targetItem.PlayItem.Id +
                                                    ".cache", CreationCollisionOption.ReplaceExisting);
                                            var downloadOperation = Downloader.CreateDownload(new Uri(playUrl), destinationFile);
                                            resultFile = await HandleDownloadAsync(downloadOperation, targetItem);
                                        }
                                        var exists = await destinationFolder.FileExistsAsync(resultFile.Name);
                                        if (resultFile != null && exists)
                                        {
                                            _mediaSource = MediaSource.CreateFromStorageFile(resultFile);
                                        }
                                        else
                                        {
                                            _mediaSource = MediaSource.CreateFromUri(new Uri(playUrl)); //如果你很急的话那先听在线的凑活下
                                        }
                                    }
                                }
                                catch
                                {
                                    var playUrl = await GetNowPlayingUrl(targetItem);
                                    if (playUrl != null)
                                        _mediaSource = MediaSource.CreateFromUri(new Uri(playUrl));
                                }
                            }
                        }
                        else
                        {
                            var playUrl = await GetNowPlayingUrl(targetItem);
                            if (Common.Setting.EnablePreLoad)
                            {
                                var reference = RandomAccessStreamReference.CreateFromUri(new Uri(playUrl));
                                using var stream = await reference.OpenReadAsync();
                                var buffer = new Buffer((uint)stream.Size);
                                await stream.ReadAsync(buffer, (uint)stream.Size, InputStreamOptions.None);
                                _ncmPlayableStream = new InMemoryRandomAccessStream();
                                await _ncmPlayableStream.WriteAsync(buffer);
                                _mediaSource = MediaSource.CreateFromStream(_ncmPlayableStream, stream.ContentType);
                            }
                            else
                            {
                                _mediaSource = MediaSource.CreateFromUri(new Uri(playUrl));
                            }
                        }
                    }

                    break;
                case HyPlayItemType.Local:
                case HyPlayItemType.LocalProgressive:
                    if (targetItem.PlayItem.DontSetLocalStorageFile == null && targetItem.PlayItem.Url != null)
                    {
                        targetItem.PlayItem.DontSetLocalStorageFile =
                            await StorageFile.GetFileFromPathAsync(targetItem.PlayItem.Url);
                    }

                    if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
                    {
                        await LoadNCMFile(targetItem);
                        _mediaSource = MediaSource.CreateFromStream(_ncmPlayableStream, _ncmPlayableStreamMIMEType);
                    }
                    else
                    {
                        await LoadLocalFile(targetItem);
                        _mediaSource = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
                    }

                    break;
                default:
                    _mediaSource = null;
                    break;
            }

            _mediaSource?.CustomProperties.Add("nowPlayingItem", targetItem);
            NowPlayingHashCode = targetItem.GetHashCode();
            MediaSystemControls.IsEnabled = true;
            await _mediaSource.OpenAsync();
            var duration = _mediaSource.Duration?.TotalMilliseconds;
            if (duration != null)
            {
                if (targetItem.PlayItem.LengthInMilliseconds != duration.Value)
                {
                    targetItem.PlayItem.LengthInMilliseconds = duration.Value;
                }
            }

            Player.Source = _mediaSource;
        }
        catch (Exception e)
        {
            Player.Source = null;
            PlayerOnMediaFailed(e.Message);
        }
    }

    private static async Task<IStorageFile> HandleDownloadAsync(DownloadOperation dl, HyPlayItem item)
    {
        var process = new Progress<DownloadOperation>(ProgressCallback);
        try
        {
            DownloadOperations.Add(item, dl);
            await dl.StartAsync().AsTask(process);
            DownloadOperations.Remove(item);
            return dl.ResultFile;
        }
        catch (Exception E)
        {
            Common.AddToTeachingTipLists("下载错误 " + E.Message);
            DownloadOperations.Remove(item);
            return null;
        }
    }

    private static void ProgressCallback(DownloadOperation obj)
    {
        if (obj.Progress.TotalBytesToReceive == 0)
        {
            Common.AddToTeachingTipLists("缓存文件下载错误", "下载错误 " + obj.CurrentWebErrorStatus);
            return;
        }
    }

    public static async void Player_SourceChanged(MediaPlayer sender, object args)
    {
        if (List.Count <= NowPlaying) return;
        if (sender.Source == null || NowPlayingItem.PlayItem == null)
        {
            return;
        }

        var hashCodeWhenRequested = NowPlayingHashCode;
        var playItemWhenRequested = NowPlayingItem;
        _ = SongFadeRequest(SongFadeEffectType.NextFadeIn);
        //当加载一个新的播放文件时,此时你应当加载歌词和 SystemMediaTransportControls
        //加载 SystemMediaTransportControls
        if (NowPlayingItem.PlayItem != null)
        {
            _controlsDisplayUpdater.Type = MediaPlaybackType.Music;
            _controlsDisplayUpdater.MusicProperties.Artist = NowPlayingItem.PlayItem.ArtistString;
            _controlsDisplayUpdater.MusicProperties.AlbumTitle = NowPlayingItem.PlayItem.AlbumString;
            _controlsDisplayUpdater.MusicProperties.Title = NowPlayingItem.PlayItem.Name;
            _controlsDisplayUpdater.MusicProperties.TrackNumber = (uint)NowPlaying;
            _controlsDisplayUpdater.MusicProperties.AlbumTrackCount = (uint)List.Count;
            _controlsDisplayUpdater.MusicProperties.Genres.Clear();
            _controlsDisplayUpdater.Thumbnail = null;
            if (NowPlayingItem.ItemType == HyPlayItemType.Netease)
                _controlsDisplayUpdater.MusicProperties.Genres.Add("NCM-" + NowPlayingItem.PlayItem.Id);
            // 第一次刷新, 以便热词切歌词
            _controlsDisplayUpdater.Update();

            //记录下当前播放位置
            ApplicationData.Current.LocalSettings.Values["nowSongPointer"] = NowPlaying.ToString();
            if (hashCodeWhenRequested == NowPlayingHashCode)
            {
                NotifyPlayItemChanged(playItemWhenRequested);
            }

            // 图片加载放在之后
            if (CoverStream.Size == 0 && !Common.Setting.noImage)
            {
                await RefreshAlbumCover();
            }

            if (CoverStream.Size != 0)
            {
                if ((hashCodeWhenRequested == NowPlayingHashCode) && !Common.Setting.noImage)
                {
                    CoverStream.Seek(0);
                    OnSongCoverChanged?.Invoke(hashCodeWhenRequested, CoverBuffer);
                }
            }

            if (hashCodeWhenRequested == NowPlayingHashCode)
            {
                //加载歌词
                _ = LoadLyrics(playItemWhenRequested);
            }

            //更新磁贴
            if (hashCodeWhenRequested == NowPlayingHashCode)
            {
                CoverStream.Seek(0);
                await RefreshTile(hashCodeWhenRequested, playItemWhenRequested, CoverStream);
            }

            if (hashCodeWhenRequested == NowPlayingHashCode)
            {
                // RASR 罪大恶极，害的磁贴怨声载道
                CoverStream.Seek(0);
                _controlsDisplayUpdater.Thumbnail = CoverStreamReference;
                _controlsDisplayUpdater.Update();
            }
            //这里要判断这么多次的原因在于如果只判断一次的话，后面如果切歌是无法知晓的。所以只能用这个蠢方法
        }
    }

    public static async Task RefreshAlbumCover()
    {
        try
        {
            if (NowPlayingItem.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
            {
                if (NowPlayingStorageFile != null)
                {
                    if (!Common.Setting.useTaglibPicture || NowPlayingItem.PlayItem.LocalFileTag is null ||
                        NowPlayingItem.PlayItem.LocalFileTag.Pictures.Length == 0)
                    {
                        if (NowPlayingStorageFile != null)
                        {
                            using var thumbnail =
                                await NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.MusicView, 3000);
                            var buffer = new Buffer((uint)thumbnail.Size);
                            await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                            await CoverStream.WriteAsync(buffer);
                        }
                        else
                        {
                            var file = await StorageFile.GetFileFromPathAsync("/Assets/icon.png");
                            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 3000);
                            var buffer = new Buffer((uint)thumbnail.Size);
                            await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                            await CoverStream.WriteAsync(buffer);
                        }
                    }
                    else
                    {
                        var bufferByte = NowPlayingItem.PlayItem.LocalFileTag.Pictures[0].Data.Data;
                        var buffer = bufferByte.AsBuffer();
                        await CoverStream.WriteAsync(buffer);
                    }
                }
            }
            else
            {
                string param = Common.IsInImmersiveMode
                    ? StaticSource.PICSIZE_IMMERSIVEMODE_COVER
                    : StaticSource.PICSIZE_AUDIO_PLAYER_COVER;
                using var result =
                    await Common.HttpClient.GetAsync(new Uri(NowPlayingItem.PlayItem.Album.cover + "?param=" + param));
                if (!result.IsSuccessStatusCode)
                {
                    throw new Exception("更新SMTC图片时发生异常");
                }

                var buffer = (await result.Content.ReadAsByteArrayAsync()).AsBuffer();
                CoverBuffer = buffer;
                CoverStream.Size = 0;
                CoverStream.Seek(0);
                await CoverStream.WriteAsync(buffer);
            }
        }
        catch (Exception)
        {
            //ignore
        }
    }

    public static void NotifyPlayItemChanged(HyPlayItem targetItem)
    {
        OnPlayItemChange?.Invoke(targetItem);
    }

    public static async Task RefreshTile(int hashCode, HyPlayItem targetItem, IRandomAccessStream coverStream)
    {
        try
        {
            if (targetItem?.PlayItem == null || !Common.Setting.enableTile) return;
            string fileName = targetItem.PlayItem.IsLocalFile
                ? null
                : targetItem.PlayItem.Album.id;
            bool coverStreamIsAvailable = coverStream.Size != 0 && fileName != null && fileName != "0" &&
                                          NowPlayingHashCode == hashCode;
            bool localCoverIsAvailable = false;
            string downloadLink = string.Empty;
            if (Common.Setting.saveTileBackgroundToLocalFolder
                && Common.Setting.tileBackgroundAvailability
                && !targetItem.PlayItem.IsLocalFile
                && coverStreamIsAvailable)
            {
                downloadLink = targetItem.PlayItem.Album.cover;
                StorageFolder storageFolder =
                    await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("LocalTileBackground",
                        CreationCollisionOption.OpenIfExists);
                var storageFile =
                    await storageFolder.CreateFileAsync(fileName + ".jpg", CreationCollisionOption.OpenIfExists);
                var properties = await storageFile.GetBasicPropertiesAsync();
                if (properties.Size == 0)
                {
                    using var outputStream = await storageFile.OpenAsync(FileAccessMode.ReadWrite);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(coverStream);
                    using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                    BitmapEncoder encoder =
                        await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);
                    encoder.SetSoftwareBitmap(softwareBitmap);
                    await encoder.FlushAsync();
                    if (hashCode != NowPlayingHashCode)
                    {
                        await storageFile.DeleteAsync();
                    }
                    else
                    {
                        localCoverIsAvailable = true;
                    }
                }
                else
                {
                    localCoverIsAvailable = true;
                }
            }

            var cover = Common.Setting.tileBackgroundAvailability && !targetItem.PlayItem.IsLocalFile &&
                        localCoverIsAvailable
                ? new TileBackgroundImage()
                {
                    Source = Common.Setting.saveTileBackgroundToLocalFolder && coverStreamIsAvailable
                        ? "ms-appdata:///temp/LocalTileBackground/" + fileName + ".jpg"
                        : downloadLink,
                    HintOverlay = 50
                }
                : null;
            var tileContent = new TileContent()
            {
                Visual = new TileVisual()
                {
                    DisplayName = "HyPlayer 正在播放",
                    TileSmall = new TileBinding()
                    {
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                        }
                    },
                    TileMedium = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 2
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 2
                                }
                            }
                        }
                    },
                    TileWide = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 3
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle
                                }
                            }
                        }
                    },
                    TileLarge = new TileBinding()
                    {
                        Branding = TileBranding.NameAndLogo,
                        Content = new TileBindingContentAdaptive()
                        {
                            BackgroundImage = cover,
                            Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.Name,
                                    HintStyle = AdaptiveTextStyle.Base
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.ArtistString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle,
                                    HintWrap = true,
                                    HintMaxLines = 3
                                },
                                new AdaptiveText()
                                {
                                    Text = targetItem?.PlayItem.AlbumString,
                                    HintStyle = AdaptiveTextStyle.CaptionSubtle
                                }
                            }
                        }
                    }
                }
            };

            // Create the tile notification
            var tileNotif = new TileNotification(tileContent.GetXml());

            // And send the notification to the primary tile
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tileNotif);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("更新磁贴时发生错误", ex.Message);
        }
    }

    private static async void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        OnPlayPositionChange?.Invoke(Player.PlaybackSession.Position);
        LoadLyricChange();
        await CheckMediaTimeRemaining();
    }

    public static async Task CheckMediaTimeRemaining()
    {
        if (NowPlayingItem.PlayItem == null) return;
        var nextFadeTime = TimeSpan.FromSeconds(Common.Setting.fadeNextTime);
        if (!Common.Setting.advFade)
        {
            AdvFadeVolume = 1;
            if (Player.PlaybackSession.Position.TotalMilliseconds >=
                NowPlayingItem.PlayItem.LengthInMilliseconds - nextFadeTime.TotalMilliseconds)
            {
                UserRequestedChangingSong = false;
                await SongFadeRequest(SongFadeEffectType.AutoNextFadeOut);
            }
            else if (AutoFadeProcessing)
            {
                AutoFadeProcessing = false;
                FadeLocked = false;
                await SongFadeRequest(SongFadeEffectType.PlayFadeIn);
#if DEBUG
                Debug.WriteLine("Unlocked");
#endif
            }
        }
        else
        {
            if (Player.PlaybackSession.Position.TotalMilliseconds >=
                NowPlayingItem.PlayItem.LengthInMilliseconds - nextFadeTime.TotalMilliseconds)
            {
                await SongFadeRequest(SongFadeEffectType.AdvFadeOut);
            }
            else if (AutoFadeProcessing)
            {
                AutoFadeProcessing = false;
                AdvFadeVolume = 1;
                AdvFadeProcessStatus = false;
                VolumeChangeProcess();
            }
        }
    }

    static Timer highTimer = new Timer(10);

    private static void LoadLyricChange()
    {
        if (LyricInfo.Lyrics.Count == 0) return;
        if (LyricPos >= LyricInfo.Lyrics.Count || LyricPos < 0) LyricPos = 0;
        var changed = false;
        var realPos = Player.PlaybackSession.Position - LyricOffset;
        if (LyricInfo.Lyrics[LyricPos].LyricLine.StartTime > realPos) //当感知到进度回溯时执行
        {
            LyricPos = LyricInfo.Lyrics.FindLastIndex(t => t.LyricLine.StartTime <= realPos) - 1;
            if (LyricPos == -2) LyricPos = -1;
            changed = true;
        }

        try
        {
            if (LyricPos == 0 && LyricInfo.Lyrics.Count != 1) changed = false;
            while (LyricInfo.Lyrics.Count > LyricPos + 1 &&
                   LyricInfo.Lyrics[LyricPos + 1].LyricLine.StartTime <= realPos) //正常的滚歌词
            {
                LyricPos++;
                changed = true;
            }
        }
        catch
        {
            // ignored
        }


        if (changed)
        {
            OnLyricChange?.Invoke();
        }
    }

    private static void Player_CurrentStateChanged(MediaPlayer sender, object args)
    {
        //先通知 SystemMediaTransportControls
        MediaSystemControls.PlaybackStatus = Player.PlaybackSession.PlaybackState switch
        {
            MediaPlaybackState.Playing => MediaPlaybackStatus.Playing,
            MediaPlaybackState.Paused => MediaPlaybackStatus.Paused,
            _ => MediaSystemControls.PlaybackStatus
        };

        if (Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            OnPlay?.Invoke();
        else
            OnPause?.Invoke();
    }

    private static async Task LoadLyrics(HyPlayItem hpi)
    {
        var pureLyricInfo = new PureLyricInfo();
        var unionTranslation = false;
        switch (hpi.ItemType)
        {
            case HyPlayItemType.Netease:
                pureLyricInfo = await LoadNcLyric(hpi);
                break;
            case HyPlayItemType.Local:
                try
                {
                    var folder =
                        StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(NowPlayingItem.PlayItem.Url));
                    var fileName = Path.GetFileNameWithoutExtension(NowPlayingItem.PlayItem.Url);
                    pureLyricInfo = new PureLyricInfo
                    {
                        PureLyrics = await FileIO.ReadTextAsync(
                            await StorageFile.GetFileFromPathAsync(Path.ChangeExtension(NowPlayingItem.PlayItem.Url,
                                "lrc")))
                    };
                    unionTranslation = true;
                }
                catch
                {
                    pureLyricInfo = new PureLyricInfo();
                }

                break;
        }

        //先进行歌词转换以免被搞
        if (pureLyricInfo is not KaraokLyricInfo || !Common.Setting.karaokLyric)
        {
            LyricInfo.Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics, unionTranslation);
        }
        else
        {
            LyricInfo.Lyrics = Utils.ConvertKaraok(pureLyricInfo);
        }

        if (LyricInfo.Lyrics.Count == 0)
        {
            if (Common.Setting.showComposerInLyric)
                LyricInfo.Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(pureLyricInfo.PureLyrics, TimeSpan.Zero)
                });
        }
        else
        {
            if (pureLyricInfo is not KaraokLyricInfo karaoke) Utils.ConvertTranslation(pureLyricInfo.TrLyrics, LyricInfo.Lyrics);
            else Utils.ConvertYrcTranslation(karaoke, LyricInfo.Lyrics);
            await Utils.ConvertRomaji(pureLyricInfo, LyricInfo.Lyrics);

            if (LyricInfo.Lyrics.Count != 0 && LyricInfo.Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
                LyricInfo.Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, TimeSpan.Zero) });
        }

        LyricInfo.LyricMetadata = pureLyricInfo.LyricMetadata;
        LyricInfo.SongMetadata = pureLyricInfo.SongMetadata;
        LyricInfo.PureLyricInfo = pureLyricInfo;

        LyricPos = 0;

        OnLyricLoaded?.Invoke();
        OnLyricChange?.Invoke();

        try
        {
            if (Common.Setting.enableAmllTtmlDb && hpi.ItemType == HyPlayItemType.Netease)
            {
                var ttml = await Common.HttpClient!.GetStringAsync(
                    $"https://gcore.jsdelivr.net/gh/Steve-xmh/amll-ttml-db@main/ncm-lyrics/{hpi.PlayItem.Id}.ttml");
                var ttmlConverter = new AppleSyllableConverter();
                var lrcConverter = new LrcConverter();
                var lrcTranslationConverter = new LrcTranslationEnhancer();
                var alrc = ttmlConverter.Convert(ttml);
                var lrc = lrcConverter.ConvertBack(alrc);
                var trLrc = lrcTranslationConverter.Enhance(alrc);
                ALRCLyricInfo ttmlLyric = new ALRCLyricInfo()
                {
                    PureLyrics = lrc,
                    TrLyrics = trLrc,
                    alrc = alrc,
                    LyricMetadata = [
                        new LyricInfoMetadata
                    {
                        Key = "lyric_user",
                        Value = alrc.LyricInfo?.Author,
                        DisplayName = "歌词作者",
                        ActionUri = $"https://github.com/{alrc.LyricInfo?.Author}"
                    },
                    new LyricInfoMetadata
                    {
                        Key = "source",
                        Value = "amll-ttml-db",
                        DisplayName = "歌词来源",
                        ActionUri = $"https://github.com/Steve-xmh/amll-ttml-db/blob/main/ncm-lyrics/{hpi.PlayItem.Id}.ttml"
                    }
                    ],
                    SongMetadata = []
                };

                LyricInfo.Lyrics = Utils.ConvertPureLyric(ttmlLyric.PureLyrics, true);
                Utils.ConvertTranslation(ttmlLyric.TrLyrics, LyricInfo.Lyrics);
                LyricInfo.LyricMetadata = ttmlLyric.LyricMetadata;
                LyricInfo.SongMetadata = ttmlLyric.SongMetadata;
                LyricInfo.PureLyricInfo = ttmlLyric;

                OnLyricLoaded?.Invoke();
                OnLyricChange?.Invoke();
            }
        }
        catch
        {
            // ignore
        }
    }



    private static async Task<PureLyricInfo> LoadNcLyric(HyPlayItem ncp)
    {
        try
        {
            if (ncp.ItemType != HyPlayItemType.Netease ||
                ncp.PlayItem == null)
                return new PureLyricInfo
                {
                    PureLyrics = "[00:00.000] 无歌词 请欣赏",
                    TrLyrics = null
                };
            try
            {
                PureLyricInfo res = new PureLyricInfo();
                var lyricRequest = new LyricRequest() { Id = ncp.PlayItem.Id };
                var lyricResult = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.LyricApi, lyricRequest);
                string lrc, romaji, karaoklrc, translrc, yrromaji, yrtranslrc;
                if (lyricResult.IsError)
                {
                    Common.AddToTeachingTipLists("获取歌词失败", lyricResult.Error.Message);
                    return new PureLyricInfo
                    {
                        PureLyrics = "[00:00.000] 歌词获取失败",
                        TrLyrics = null
                    };
                }

                if (lyricResult.Value?.Lyric is null && lyricResult.Value?.YunLyric is null)
                {
                    return new PureLyricInfo
                    {
                        PureLyrics = "[00:00.000] 无歌词 请欣赏",
                        TrLyrics = null
                    };
                }

                string CleanLrc(string text)
                {
                    return string.Join('\n',
                        text.Split("\n")
                        .Where(t => !t.StartsWith("{")).ToArray());
                }


                if (lyricResult.Value?.YunLyric?.Lyric is null)
                {
                    lrc = CleanLrc(lyricResult.Value?.Lyric?.Lyric);
                    romaji = lyricResult.Value?.RomajiLyric?.Lyric;
                    translrc = lyricResult.Value?.TranslationLyric?.Lyric;
                    res = new PureLyricInfo()
                    {
                        PureLyrics = lrc,
                        TrLyrics = translrc,
                        NeteaseRomaji = romaji,
                    };
                }
                else
                {
                    lrc = CleanLrc(lyricResult.Value?.Lyric?.Lyric);
                    karaoklrc = CleanLrc(lyricResult.Value?.YunLyric?.Lyric);
                    yrromaji = lyricResult.Value?.YunRomajiLyric?.Lyric;
                    yrtranslrc = lyricResult.Value?.YunTranslationLyric?.Lyric;
                    romaji = lyricResult.Value?.RomajiLyric?.Lyric;
                    translrc = lyricResult.Value?.TranslationLyric?.Lyric;
                    res = new KaraokLyricInfo()
                    {
                        PureLyrics = lrc,
                        TrLyrics = translrc,
                        YrNeteaseRomaji = yrromaji,
                        YrTrLyrics = yrtranslrc,
                        NeteaseRomaji = romaji,
                        KaraokLyric = karaoklrc
                    };
                }

                // add metadata
                // 添加翻译作词信息
                if (lyricResult.Value?.LyricUser?.UserId is not null)
                {
                    res.LyricMetadata.Add(new LyricInfoMetadata()
                    {
                        Key = "lyric_user",
                        Value = lyricResult.Value.LyricUser.Nickname,
                        ActionUri = $"hyplayer://us{lyricResult.Value.LyricUser.UserId}",
                        DisplayName = "歌词贡献者"
                    });
                }

                if (lyricResult.Value?.TranslationUser?.UserId is not null)
                {
                    res.LyricMetadata.Add(new LyricInfoMetadata()
                    {
                        Key = "translation_user",
                        Value = lyricResult.Value.TranslationUser.Nickname,
                        ActionUri = $"hyplayer://us{lyricResult.Value.TranslationUser.UserId}",
                        DisplayName = "翻译贡献者"
                    });
                }

                return res;
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        catch
        {
            return new PureLyricInfo();
        }

        return new PureLyricInfo();
    }

    public static async Task OnAudioRenderDeviceChangedOrInitialized()
    {
        try
        {
            if (string.IsNullOrEmpty(Common.Setting.AudioRenderDevice)) Player.AudioDevice = null;
            else Player.AudioDevice = await DeviceInformation.CreateFromIdAsync(Common.Setting.AudioRenderDevice);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("在切换输出设备时发生错误", ex.Message);
            Player.AudioDevice = null;
        }
    }
    /********        播放文件相关        ********/

    public static HyPlayItem AppendNcSong(NCSong ncSong, int position = -1)
    {
        var hpi = LoadNcSong(ncSong);
        if (List.Contains(hpi))
        {
            return hpi;
        }

        if (position < 0)
            position = List.Count;
        if (hpi != null)
            List.Insert(position, hpi);
        SongAppendDone();
        return hpi;
    }

    public static List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        if (position < 0)
            position = List.Count;
        var insertList = ncSongs.Select(LoadNcSong).Where(t => !List.Contains(t)).ToList();
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
        {
            insertList = insertList.Except(List, new HyPlayerItemComparer()).ToList();
            // 防止重新打乱列表
            if (insertList.Count <= 0)
            {
                return insertList;
            }
        }
        List.InsertRange(position, insertList);
        SongAppendDone();
        return insertList;
    }

    public static HyPlayItem LoadNcSong(NCSong ncSong)
    {
        try
        {
            var ncp = NCSongToPlayItem(ncSong);
            return LoadNcPlayItem(ncp);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return null;
    }

    public static void AppendNcPlayItem(PlayItem ncp)
    {
        var hpi = LoadNcPlayItem(ncp);
        List.Add(hpi);
    }

    private static HyPlayItem LoadNcPlayItem(PlayItem ncp)
    {
        var hpi = new HyPlayItem
        {
            ItemType = ncp.Type,
            PlayItem = ncp
        };
        return hpi;
    }

    public static PlayItem NCSongToPlayItem(NCSong ncSong)
    {
        return new PlayItem
        {
            Type = ncSong.Type,
            InfoTag = ncSong.alias,
            Album = ncSong.Album,
            Artist = ncSong.Artist,
            //SubExt = token["type"].ToString(),
            Id = ncSong.sid,
            Name = ncSong.songname,
            TrackId = ncSong.TrackId,
            CDName = ncSong.CDName,
            //url = token["url"].ToString(),
            LengthInMilliseconds = ncSong.LengthInMilliseconds
            //size = token["size"].ToString(),
            //md5 = token["md5"].ToString()
        };
    }

    public static void AppendNcSongs(IList<NCSong> ncSongs, bool needRemoveList = true, bool resetPlaying = true,
        string currentSongId = "-1")
    {
        if (ncSongs == null) return;
        if (needRemoveList)
            RemoveAllSong(resetPlaying);
        try
        {
            foreach (var ncSong in ncSongs)
            {
                var ncp = NCSongToPlayItem(ncSong);
                AppendNcPlayItem(ncp);
            }

            SongAppendDone(currentSongId);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    public static async Task<bool> AppendNcSource(string sourceId)
    {
        /*  歌单: pl + 歌单ID (e.g. pl123456)
         *  单曲: ns + 歌曲ID (e.g. ns1515584)
         *  专辑: al + 专辑ID(e.g.al552255)
         *  歌手热门: sh + 歌手ID(e.g sh25151)
         *  歌手全部: sa + 歌手ID e.g.sa245144
         *  电台: rd + 电台ID  e.g.rd5274522
         *  最近播放: rc + 随机数字
         */
        try
        {
            var prefix = sourceId.Substring(0, 2);
            switch (prefix)
            {
                case "pl":
                    await AppendPlayList(sourceId.Substring(2, sourceId.Length - 2));
                    return true;
                case "ns":
                    var result = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.SongDetailApi,
                        new SongDetailRequest()
                        {
                            Id = sourceId.Substring(2, sourceId.Length - 2)
                        });
                    if (result.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌曲信息失败", result.Error.Message);
                        return false;
                    }
                    else
                    {
                        if (result.Value?.Songs is not { Length: > 0 })
                        {
                            Common.AddToTeachingTipLists("获取歌曲信息失败", "歌曲信息为空");
                            return false;
                        }
                        AppendNcSong(result.Value.Songs?[0].MapToNcSong());
                    }
                    return true;
                case "al":
                    await AppendAlbum(sourceId.Substring(2, sourceId.Length - 2));
                    return true;
                case "sh":
                    await AppendSingerHot(sourceId.Substring(2, sourceId.Length - 2));
                    return true;
                case "sa":
                    await AppendSingerHot(sourceId.Substring(2, sourceId.Length - 2));
                    return true;
                case "rd":
                    await AppendRadioList(sourceId.Substring(2, sourceId.Length - 2));
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            return false;
        }
    }

    private static async Task<bool> AppendSingerHot(string id)
    {
        try
        {
            var j1 = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.ArtistTopSongApi,
                new ArtistTopSongRequest
                {
                    ArtistId = id
                });
            if (j1.IsError)
            {
                Common.AddToTeachingTipLists("获取歌手热门歌曲失败", j1.Error.Message);
                return false;
            }

            var list = j1.Value.Songs?.Select(t => t.MapToNcSong()).ToList();
            AppendNcSongs(list, false);
            return true;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return false;
    }

    private static async Task<bool> AppendAlbum(string albumId)
    {
        try
        {
            var json = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.AlbumApi,
                new AlbumRequest()
                {
                    Id = albumId
                });


            if (json.IsError)
            {
                Common.AddToTeachingTipLists("获取专辑信息失败", json.Error.Message);
                return false;
            }
            AppendNcSongs(json.Value.Songs?.Select(t => t.MapToNcSong()).ToList(), false);
            return true;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return false;
    }

    public static async Task<bool> AppendRadioList(string radioId, bool asc = false)
    {
        try
        {
            bool? hasMore = true;
            var page = 0;
            while (hasMore is true)
                try
                {
                    var json = await Common.NeteaseAPI?.RequestAsync(NeteaseApis.DjChannelProgramsApi,
                        new DjChannelProgramsRequest()
                        {
                            RadioId = radioId,
                            Offset = page * 100,
                            Limit = 100,
                            Asc = asc
                        });
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("获取电台节目失败", json.Error.Message);
                        return false;
                    }

                    hasMore = json.Value is { More: true };
                    if (json.Value?.Programs is { Length: > 0 })
                        AppendNcSongs(
                            json.Value.Programs.Select(t => (NCSong)t.MapToNCFmItem()).ToList(),
                            false);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }

            return true;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return false;
    }

    public static async Task<bool> AppendPlayList(string playlistId)
    {
        try
        {
            var detailResponse = await Common.NeteaseAPI.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                new PlaylistTracksGetRequest() { Id = playlistId });

            var nowIndex = 0;
            if (detailResponse.IsError)
            {
                Common.AddToTeachingTipLists("获取歌单失败", detailResponse.Error.Message);
                return false;
            }
            var trackIds = detailResponse.Value.Playlist?.TrackIds?.Select(t => t.Id).ToList() ?? [];
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    var songResponse = await Common.NeteaseAPI.RequestAsync(NeteaseApis.SongDetailApi,
                         new SongDetailRequest() { IdList = nowIds });
                    if (songResponse.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌曲失败", songResponse.Error.Message);
                    }
                    nowIndex++;
                    var privileges = songResponse.Value?.Privileges ?? [];
                    var songs = songResponse.Value?.Songs ?? [];
                    var result = new List<NCSong>();
                    if (privileges is null) return false;
                    for (var i = 0; i < privileges.Length; i++)
                    {
                        if (privileges[i].St == 0)
                        {
                            result.Add(songs[i].MapToNcSong());
                        }
                    }
                    AppendNcSongs(result, false);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return false;
    }

    public static async Task<bool> AppendStorageFile(StorageFile sf, bool nocheck163 = false)
    {
        List.Add(await LoadStorageFile(sf));
        return true;
    }

    public static async Task<HyPlayItem> LoadStorageFile(StorageFile sf, bool nocheck163 = false)
    {
        var abstraction = new UwpStorageFileAbstraction(sf);
        var tagFile = File.Create(abstraction);
        if (nocheck163 ||
            !The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var mi))
        {
            //TagLib.File afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            var songPerformersList = tagFile.Tag.Performers
                .Select(t => new NCArtist { name = t, Type = HyPlayItemType.Local }).ToList();
            if (songPerformersList.Count == 0)
            {
                songPerformersList.Add(new NCArtist { name = "未知歌手", Type = HyPlayItemType.Local });
            }

            var hyPlayItem = new HyPlayItem
            {
                PlayItem = new PlayItem
                {
                    IsLocalFile = true,
                    LocalFileTag = tagFile.Tag,
                    Bitrate = tagFile.Properties.AudioBitrate,
                    InfoTag = sf.Provider.DisplayName,
                    Id = null,
                    Name = tagFile.Tag.Title,
                    Type = HyPlayItemType.Local,
                    Artist = songPerformersList,
                    Album = new NCAlbum
                    {
                        name = tagFile.Tag.Album
                    },
                    TrackId = (int)tagFile.Tag.Track,
                    CDName = "01",
                    Url = sf.Path,
                    SubExt = sf.FileType,
                    Size = "0",
                    LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds
                },
                ItemType = HyPlayItemType.Local
            };
            if (sf.Provider.Id == "network" || Common.Setting.safeFileAccess)
                hyPlayItem.PlayItem.DontSetLocalStorageFile = sf;
            tagFile.Dispose();
            abstraction.Dispose();
            return hyPlayItem;
        }

        if (string.IsNullOrEmpty(mi.musicName)) return await LoadStorageFile(sf, true);

        var hpi = new PlayItem
        {
            Album = new NCAlbum
            {
                name = mi.album,
                id = mi.albumId.ToString(),
                cover = mi.albumPic
            },
            Url = sf.Path,
            SubExt = sf.FileType,
            LocalFileTag = tagFile.Tag,
            Bitrate = mi.bitrate,
            IsLocalFile = true,
            Type = HyPlayItemType.Netease,
            LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds,
            Id = mi.musicId.ToString(),
            Artist = null,
            Name = mi.musicName,
            TrackId = (int)tagFile.Tag.Track,
            CDName = "01",
            InfoTag = sf.Provider.DisplayName
        };
        hpi.Artist = mi.artist
            .Select(t => new NCArtist { name = t[0].ToString(), id = t[1].ToString() })
            .ToList();
        if (sf.Provider.Id == "network")
            hpi.DontSetLocalStorageFile = sf;
        tagFile.Dispose();
        abstraction.Dispose();
        return new HyPlayItem
        {
            ItemType = HyPlayItemType.Local,
            PlayItem = hpi
        };
    }

    public static Task CreateShufflePlayLists(string currentSongId = "-1")
    {
        ShuffleList.Clear();
        ShufflingIndex = 0;
        if (List.Count != 0)
        {
            HashSet<int> shuffledNumbers = new();
            bool hasSpecifiedCorrectCurrentSong = false;
            if (currentSongId != "-1")
            {
                int playItemIndex = List.FindIndex(s => s.ToNCSong().sid == currentSongId);
                if (playItemIndex != -1)
                {
                    shuffledNumbers.Add(playItemIndex);
                    ShuffleList.Add(playItemIndex);
                    hasSpecifiedCorrectCurrentSong = true;
                }
            }

            while (shuffledNumbers.Count < List.Count)
            {
                var buffer = Guid.NewGuid().ToByteArray();
                var seed = BitConverter.ToInt32(buffer, 0);
                var random = new Random(seed);
                var indexShuffled = random.Next(List.Count);
                if (shuffledNumbers.Add(indexShuffled))
                    ShuffleList.Add(indexShuffled);
            }

            if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
            {
                ShufflingIndex = hasSpecifiedCorrectCurrentSong ? 0 : ShuffleList.IndexOf(NowPlaying);
            }
        }

        // Call 一下来触发前端显示的播放列表更新
        _ = Common.Invoke(() => OnPlayListAddDone?.Invoke());
        return Task.CompletedTask;
    }

    public static void CheckABTimeRemaining(TimeSpan currentTime)
    {
        if (currentTime >= Common.Setting.ABEndPoint && Common.Setting.ABEndPoint != TimeSpan.Zero &&
            Common.Setting.ABEndPoint > Common.Setting.ABStartPoint)
            Seek(Common.Setting.ABStartPoint);
    }

    public static async void UpdateLastFMNowPlayingAsync(HyPlayItem NowPlayingItem)
    {
        if (NowPlayingItem?.PlayItem != null && NowPlayingItem.ItemType == HyPlayItemType.Netease)
        {
            try
            {
                await LastFMManager.UpdateNowPlayingAsync(NowPlayingItem);
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists("同步Last.FM正在播放信息时发生错误", ex.Message);
            }
        }
    }
}

public enum PlayMode
{
    DefaultRoll,
    SinglePlay,
    Shuffled
}

public static class Utils
{
    public static List<SongLyric> ConvertPureLyric(string lyricAllText, bool hasTranslationsInLyricText = false)
    {
        using var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        return parsedlyrics.Lines.OrderBy(t => t.StartTime).Select(lyricsLine => new SongLyric
        { LyricLine = lyricsLine, Translation = null })
            .ToList();
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        using var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime))
            {
                songLyric.Translation = lyricsLine.CurrentLyric;
                break;
            }
    }
    public static void ConvertYrcTranslation(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics)
    {
        using var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrTrLyrics.AsSpan());
        if (Common.Setting.MigrateLyrics)
        {
            using var sourceLyrics = LrcParser.ParseLrc(lyricInfo.TrLyrics.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                                     t.LyricLine.StartTime == lyricsLine.StartTime ||
                                     t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                                     t.LyricLine.StartTime == lyricsLine.StartTime ||
                                     t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Translation = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }
    public static void ConvertNeteaseRomaji(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricAllText)) return;
        using var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        foreach (var lyricsLine in parsedlyrics.Lines)
            foreach (var songLyric in lyrics.Where(songLyric =>
                         songLyric.LyricLine.StartTime == lyricsLine.StartTime ||
                         songLyric.LyricLine?.PossibleStartTime == lyricsLine.StartTime))
            {
                songLyric.Romaji = lyricsLine.CurrentLyric;
                break;
            }
    }
    public static void ConvertYrcNeteaseRomaji(KaraokLyricInfo lyricInfo, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricInfo.NeteaseRomaji) && string.IsNullOrEmpty(lyricInfo.YrNeteaseRomaji)) return;
        using var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrNeteaseRomaji.AsSpan());
        if (Common.Setting.MigrateLyrics)
        {
            using var sourceLyrics = LrcParser.ParseLrc(lyricInfo.NeteaseRomaji.AsSpan());
            var migrated = MigrationTool.Migrate(targetLyrics, sourceLyrics);
            foreach (var lyricsLine in migrated.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                                     t.LyricLine.StartTime == lyricsLine.StartTime ||
                                     t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
        else
        {
            foreach (var lyricsLine in targetLyrics.Lines)
            {
                foreach (var lyric in lyrics.Where(t =>
                                     t.LyricLine.StartTime == lyricsLine.StartTime ||
                                     t.LyricLine?.PossibleStartTime == lyricsLine.StartTime).ToList())
                {
                    lyric.Romaji = lyricsLine.CurrentLyric;
                    break;
                }
            }
        }
    }

    public static async Task ConvertKawazuRomaji(List<SongLyric> lyrics)
    {
        if (Common.KawazuConv is null) return;
        foreach (var lyricItem in lyrics)
        {
            if (!string.IsNullOrWhiteSpace(lyricItem.LyricLine.CurrentLyric))
            {
                if (!Utilities.HasKana(lyricItem.LyricLine.CurrentLyric)) continue;
                lyricItem.Romaji =
                    await Common.KawazuConv.Convert(lyricItem.LyricLine.CurrentLyric, To.Romaji, Mode.Separated);
                if (lyricItem.LyricLine is not KaraokeLyricsLine klyric) continue;
                var list = await Common.KawazuConv.GetDivisions(lyricItem.LyricLine.CurrentLyric, To.Romaji, Mode.Separated, RomajiSystem.Hepburn, "", "");
                SetRomajiKaraoke(list, klyric.WordInfos.ToList());
            }
        }
    }
    public static void SetRomajiKaraoke(List<Division> romajiInfo, List<KaraokeWordInfo> wordInfo)
    {
        var elements = new List<JapaneseElement>();
        foreach (var division in romajiInfo)
        {
            elements.AddRange(division);
        }
        int delta = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            var curElement = elements[i].Element;
            var curHiraNotation = elements[i].HiraNotation;
        parseOneChar:
            if (i + delta >= wordInfo.Count)
            {
                if (!string.IsNullOrWhiteSpace(curHiraNotation))
                {
                    wordInfo[wordInfo.Count - 1].Transliteration += Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                }
                break;
            }
            if (curElement.Contains(wordInfo[i + delta].CurrentWords.Trim()))
            {
                wordInfo[i + delta].Transliteration = Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                if (!string.IsNullOrWhiteSpace(wordInfo[i + delta].CurrentWords))
                {
                    var trimmedWord = wordInfo[i + delta].CurrentWords.Trim();
                    var idx = curElement.IndexOf(trimmedWord, StringComparison.Ordinal);
                    if (idx >= 0)
                        curElement = curElement.Remove(idx, trimmedWord.Length);
                }

                if (curElement.Trim().Length > 0)
                {
                    wordInfo[i + delta].Transliteration = Utilities.ToRawRomaji(curHiraNotation.Substring(0, 1), RomajiSystem.Hepburn, true);
                    curHiraNotation = curHiraNotation.Substring(1);
                    delta++;
                    goto parseOneChar;
                }
            }

        }
    }
    public static async Task ConvertRomaji(PureLyricInfo pureLyricInfo, List<SongLyric> lyrics)
    {
        switch (Common.Setting.LyricRomajiSource)
        {
            case RomajiSource.None:
                break;
            case RomajiSource.AutoSelect:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo) ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                else
                    await ConvertKawazuRomaji(lyrics);
                break;
            case RomajiSource.NeteaseOnly:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo) ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                break;
            case RomajiSource.KawazuOnly:
                await ConvertKawazuRomaji(lyrics);
                break;
        }
    }

    public static List<SongLyric> ConvertKaraok(PureLyricInfo pureLyricInfo)
    {
        if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo && !string.IsNullOrEmpty(karaokLyricInfo.KaraokLyric))
        {
            using var parsedLyrics = KaraokeParser.ParseKaraoke(((KaraokLyricInfo)pureLyricInfo).KaraokLyric.AsSpan());
            if (Common.Setting.MigrateLyrics)
            {
                using var pureLyrics = LrcParser.ParseLrc(pureLyricInfo.PureLyrics.AsSpan());
                var migrated = MigrationTool.Migrate(parsedLyrics, pureLyrics);
                return migrated.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t }).ToList();
            }
            return parsedLyrics.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t }).ToList();
        }

        throw new ArgumentException("LyricInfo is not KaraokeLyricInfo");
    }
}

public class AudioDevices
{
    public string DeviceID;
    public string DeviceName;
    public bool IsDefaultDevice;
}