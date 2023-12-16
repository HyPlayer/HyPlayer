#region

using AudioEffectComponent;
using HyPlayer.Classes;
using Kawazu;
using LyricParser.Abstraction;
using LyricParser.Implementation;
using Microsoft.Toolkit.Uwp.Notifications;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
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
using Windows.Media.Streaming.Adaptive;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.Xaml.Media;
using static QRCoder.PayloadGenerator;
using Buffer = Windows.Storage.Streams.Buffer;
using File = TagLib.File;

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

    public delegate Task SongCoverChanged(int hashCode, IRandomAccessStream coverStream);

    public static int NowPlaying;
    private static readonly System.Timers.Timer SecTimer = new(1000); // 公用秒表
    public static readonly List<HyPlayItem> List = new();
    public static readonly List<int> ShuffleList = new();
    public static int ShufflingIndex = -1;
    public static List<SongLyric> Lyrics = new();
    public static TimeSpan LyricOffset = TimeSpan.Zero;
    public static PropertySet AudioEffectsProperties = new PropertySet();

    /********        API        ********/
    public static MediaPlayer Player;
    public static SystemMediaTransportControls MediaSystemControls;
    private static SystemMediaTransportControlsDisplayUpdater _controlsDisplayUpdater;
    private static readonly BackgroundDownloader Downloader = new();
    private static Dictionary<HyPlayItem, DownloadOperation> DownloadOperations = new();
    public static InMemoryRandomAccessStream CoverStream = new InMemoryRandomAccessStream();

    public static RandomAccessStreamReference CoverStreamReference =
        RandomAccessStreamReference.CreateFromStream(CoverStream);

    public static int NowPlayingHashCode = 0;
    private static InMemoryRandomAccessStream _ncmPlayableStream;
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
    private static bool _seekingTrack = false;
    private static int _currentSeekingHashCode = -1;
    private static bool _isBuffering = false;
    private static TimeSpan? _targetSeekingTimeSpan;

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
        Player.PlaybackSession.SeekCompleted += PlaybackSession_SeekCompleted;
        Player.PlaybackSession.BufferedRangesChanged += PlaybackSession_BufferedRangesChanged;
        Player.PlaybackSession.BufferingStarted += PlaybackSession_BufferingStarted;
        Player.PlaybackSession.BufferingEnded += PlaybackSession_BufferingEnded;
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
            highTimer.Elapsed += (_,_) => {LoadLyricChange();};
            highTimer.Start(); 
        }
        HistoryManagement.InitializeHistoryTrack();
        if (!Common.Setting.EnableAudioGain) AudioEffectsProperties["AudioGain_Disabled"] = true;
        Player.AddAudioEffect(typeof(AudioGainEffect).FullName, true, AudioEffectsProperties);
        Common.IsInFm = false;
    }

    private static void PlaybackSession_BufferingEnded(MediaPlaybackSession sender, object args)
    {
        MediaPlaybackSessionBufferingStartedEventArgs bufferingStartedEventArgs = args as MediaPlaybackSessionBufferingStartedEventArgs;

    }

    private static void PlaybackSession_BufferingStarted(MediaPlaybackSession sender, object args)
    {
        throw new NotImplementedException();
    }

    private static void PlaybackSession_BufferedRangesChanged(MediaPlaybackSession sender, object args)
    {
        var rangeList = sender.GetBufferedRanges();
        string ranges = "";
        foreach (var range in rangeList)
        {
            ranges += $"[{range.Start},{range.End}],";
        }
        ranges = ranges.TrimEnd(',');
        Debug.WriteLine($"MediaPlayer_PlaybackSession_BufferedRangesChanged: {ranges}");
    }

    private static async void PlaybackSession_SeekCompleted(MediaPlaybackSession sender, object args)
    {
        if (_targetSeekingTimeSpan == null) return;
        if (_currentSeekingHashCode != -1 && _targetSeekingTimeSpan?.GetHashCode() != _currentSeekingHashCode)
        {
            await Task.Delay(500);  //哇 你手速好快 但是能先慢点嘛
            Seek(_targetSeekingTimeSpan, true);
            return;
        }
        _targetSeekingTimeSpan = null;
        if (_seekingTrack) _seekingTrack = false;
        _currentSeekingHashCode = -1;
        
    }

    public static async void Seek(TimeSpan? targetTimeSpan, bool isHandler = false)
    {
        if (targetTimeSpan == null) return;
        if (_mediaSource?.IsOpen == false) 
        {
            return;
        }
        if (!_seekingTrack || isHandler)
        {
            bool _isFirstSeek = !_seekingTrack;
            _seekingTrack = true;
            _targetSeekingTimeSpan = targetTimeSpan;
            _currentSeekingHashCode = targetTimeSpan.GetHashCode();
            if(!_isFirstSeek)
            {
                bool seekable = false;
                while (!seekable)
                {
                    var ranges = Player.PlaybackSession.GetBufferedRanges();
                    if (ranges.Count == 0) seekable = true;
                    foreach (var range in ranges)
                    {
                        if (range.End >= targetTimeSpan && range.Start < targetTimeSpan)
                        {
                            seekable = true;
                            break;
                        }
                    }
                    await Task.Delay(125);
                }
                Player.PlaybackSession.Position = targetTimeSpan.Value;
            }
            else
            {
                Player.PlaybackSession.Position = targetTimeSpan.Value;
            }
        }
        else
        {
            _targetSeekingTimeSpan = targetTimeSpan;
        }
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
    private static void PlayerOnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if((uint)args.ExtendedErrorCode.HResult == 0xC00D36FA)
        {
            Common.AddToTeachingTipLists("播放失败", "无法创建媒体接收器，请检查设备是否有声音输出设备！");
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
                            Tag = file.Provider.DisplayName + " NCM"
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

    public static void LikeSong()
    {
        var isLiked = Common.LikedSongs.Contains(NowPlayingItem.PlayItem.Id);
        switch (NowPlayingItem.ItemType)
        {
            case HyPlayItemType.Netease:
            {
                _ = Api.LikeSong(NowPlayingItem.PlayItem.Id,
                    !isLiked);
                if (isLiked)
                    Common.LikedSongs.Remove(NowPlayingItem.PlayItem.Id);
                else
                    Common.LikedSongs.Add(NowPlayingItem.PlayItem.Id);
                OnSongLikeStatusChange?.Invoke(!isLiked);
                break;
            }
            case HyPlayItemType.Radio:
                _ = Common.ncapi?.RequestAsync(CloudMusicApiProviders.ResourceLike,
                    new Dictionary<string, object>
                        { { "type", "4" }, { "t", "1" }, { "id", NowPlayingItem.PlayItem.Id } });
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
            Common.Setting.songUrlLazyGet ) && targetItem.PlayItem.Id != "-1")
            try
            {
                var json = await Common.ncapi?.RequestAsync(
                    CloudMusicApiProviders.SongUrlV1,
                    new Dictionary<string, object>
                    {
                        { "id", targetItem.PlayItem.Id },
                        { "level", Common.Setting.audioRate }
                    });
                if (json["data"]?[0]?["code"]?.ToString() == "200")
                {
                    if (json["data"]?[0]?["freeTrialInfo"]?.HasValues == true && Common.Setting.jumpVipSongPlaying)
                    {
                        throw new Exception("当前歌曲为 VIP 试听, 已自动跳过");
                    }

                    playUrl = json["data"][0]["url"]?.ToString();
                    if (Common.Setting.UseHttpWhenGettingSongs && playUrl.Contains("https://"))
                    {
                        playUrl = playUrl.Replace("https://", "http://");
                    }
                    var tag = json["data"]?[0]?["level"]?.ToString() switch
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
                    targetItem.PlayItem.Tag = tag;
                    AudioEffectsProperties["AudioGain_GainValue"] = float.Parse(json["data"]?[0]?["gain"].ToString());
                    _ = Common.Invoke(() =>
                    {
                        Common.BarPlayBar.TbSongTag.Text = tag;
                        if (tag.Length > 2)
                        {
                            var backgroundbrush = new LinearGradientBrush();
                            backgroundbrush.StartPoint = new Windows.Foundation.Point(0, 0);
                            backgroundbrush.EndPoint = new Windows.Foundation.Point(1, 1);

                            backgroundbrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(255,251,251,206)});
                            backgroundbrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(255, 223, 155, 28)});

                            Common.BarPlayBar.SongInfoTag.Background=backgroundbrush;
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
                    json.RemoveAll();
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
                                    throw new Exception("File Size Not Match");
                            }
                            catch
                            {
                                try
                                {
                                    var playUrl = await GetNowPlayingUrl(targetItem);
                                    //尝试从DownloadOperation下载
                                    if (playUrl != null)
                                    {
                                        if (!DownloadOperations.ContainsKey(targetItem))
                                        {
                                            var destinationFolder =
                                                await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir);
                                            var destinationFile =
                                                await destinationFolder.CreateFileAsync(
                                                    targetItem.PlayItem.Id +
                                                    ".cache",
                                                    CreationCollisionOption.ReplaceExisting);
                                            var downloadOperation =
                                                Downloader.CreateDownload(new Uri(playUrl), destinationFile);
                                            _ = HandleDownloadAsync(downloadOperation, targetItem);
                                        }

                                        AdaptiveMediaSourceCreationResult result = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(playUrl));
                                        if(result.Status == AdaptiveMediaSourceCreationStatus.Success)
                                        {
                                            _mediaSource = MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
                                        }
                                    }
                                }
                                catch
                                {
                                    var playUrl = await GetNowPlayingUrl(targetItem);
                                    if (playUrl != null)
                                    {
                                        AdaptiveMediaSourceCreationResult result = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(playUrl));
                                        if (result.Status == AdaptiveMediaSourceCreationStatus.Success)
                                        {
                                            _mediaSource = MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
                                        }
                                    }  
                                }
                            }
                        }
                        else
                        {
                            var playUrl = await GetNowPlayingUrl(targetItem);
                            AdaptiveMediaSourceCreationResult result = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(playUrl));
                            if (result.Status == AdaptiveMediaSourceCreationStatus.Success)
                            {
                                _mediaSource = MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
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

    private static async Task HandleDownloadAsync(DownloadOperation dl, HyPlayItem item)
    {
        var process = new Progress<DownloadOperation>(ProgressCallback);
        try
        {
            DownloadOperations.Add(item, dl);
            await dl.StartAsync().AsTask(process);
            DownloadOperations.Remove(item);
        }
        catch (Exception E)
        {
            Common.AddToTeachingTipLists("下载错误 " + E.Message);
            DownloadOperations.Remove(item);
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
                    OnSongCoverChanged?.Invoke(hashCodeWhenRequested, CoverStream);
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

                await result.Content.WriteToStreamAsync(CoverStream);
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
                    var buffer = new Buffer(MIMEHelper.PICTURE_FILE_HEADER_CAPACITY);
                    coverStream.Seek(0);
                    await coverStream.ReadAsync(buffer, MIMEHelper.PICTURE_FILE_HEADER_CAPACITY,
                        InputStreamOptions.None);
                    var mime = MIMEHelper.GetPictureCodecFromBuffer(buffer);
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(mime, coverStream);
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
        if (Lyrics.Count == 0) return;
        if (LyricPos >= Lyrics.Count || LyricPos < 0) LyricPos = 0;
        var changed = false;
        var realPos = Player.PlaybackSession.Position - LyricOffset;
        if (Lyrics[LyricPos].LyricLine.StartTime > realPos) //当感知到进度回溯时执行
        {
            LyricPos = Lyrics.FindLastIndex(t => t.LyricLine.StartTime <= realPos) - 1;
            if (LyricPos == -2) LyricPos = -1;
            changed = true;
        }

        try
        {
            if (LyricPos == 0 && Lyrics.Count != 1) changed = false;
            while (Lyrics.Count > LyricPos + 1 &&
                   Lyrics[LyricPos + 1].LyricLine.StartTime <= realPos) //正常的滚歌词
            {
                LyricPos++;
                changed = true;
            }
        }
        catch
        {
            // ignored
        }

        
        if (changed) {
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
            Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics, unionTranslation);
        }
        else
        {
            Lyrics = Utils.ConvertKaraok(pureLyricInfo);
        }

        if (Lyrics.Count == 0)
        {
            if (Common.Setting.showComposerInLyric)
                Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(pureLyricInfo.PureLyrics, string.Empty, TimeSpan.Zero)
                });
        }
        else
        {
            if(pureLyricInfo is not KaraokLyricInfo karaoke) Utils.ConvertTranslation(pureLyricInfo.TrLyrics, Lyrics);
            else Utils.ConvertYrcTranslation(karaoke, Lyrics);
            await Utils.ConvertRomaji(pureLyricInfo, Lyrics);

            if (Lyrics.Count != 0 && Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
                Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, string.Empty, TimeSpan.Zero) });
        }

        LyricPos = 0;

        OnLyricLoaded?.Invoke();
        OnLyricChange?.Invoke();
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
                JObject json;

                if (Common.Setting.karaokLyric)
                {
                    json = await Common.ncapi?.RequestAsync(
                        CloudMusicApiProviders.LyricNew,
                        new Dictionary<string, object> { { "id", ncp.PlayItem.Id } });
                    string lrc, romaji, karaoklrc, translrc,yrromaji,yrtranslrc;
                    if (json["yrc"] is null)
                    {
                        lrc = string.Join('\n',
                            (json["lrc"]?["lyric"]?.ToString() ?? string.Empty).Split("\n")
                            .Where(t => !t.StartsWith("{")).ToArray());
                        romaji = json["romalrc"]?["lyric"]?.ToString();
                        translrc = json["tlyric"]?["lyric"]?.ToString();
                        return new PureLyricInfo()
                        {
                            PureLyrics = lrc,
                            TrLyrics = translrc,
                            NeteaseRomaji = romaji,
                        };
                    }
                    else
                    {
                        lrc = string.Join('\n',
                            (json["lrc"]?["lyric"]?.ToString() ?? string.Empty).Split("\n")
                            .Where(t => !t.StartsWith("{")).ToArray());
                        karaoklrc = string.Join('\n',
                            (json["yrc"]?["lyric"]?.ToString() ?? string.Empty).Split("\n")
                            .Where(t => !t.StartsWith("{")).ToArray());
                        yrromaji = json["yromalrc"]?["lyric"]?.ToString();
                        yrtranslrc = json["ytlrc"]?["lyric"]?.ToString();
                        romaji = json["romalrc"]?["lyric"]?.ToString();
                        translrc = json["tlyric"]?["lyric"]?.ToString();
                        return new KaraokLyricInfo()
                        {
                            PureLyrics = lrc,
                            TrLyrics = translrc,
                            YrNeteaseRomaji = yrromaji,
                            YrTrLyrics = yrtranslrc,
                            NeteaseRomaji = romaji,
                            KaraokLyric = karaoklrc
                        };
                    }
                }
                else
                {
                    json = await Common.ncapi?.RequestAsync(
                        CloudMusicApiProviders.Lyric,
                        new Dictionary<string, object> { { "id", ncp.PlayItem.Id } });
                    if (json["nolyric"]?.ToString().ToLower() == "true")
                        return new PureLyricInfo
                        {
                            PureLyrics = "[00:00.000] 纯音乐 请欣赏",
                            TrLyrics = null
                        };
                    if (json["uncollected"]?.ToString().ToLower() == "true")
                        /*
                             * 此接口失效
                            //Ask for Cloud Pan
                            json = await Common.ncapi?.RequestAsync(
                                CloudMusicApiProviders.CloudLyric,
                                new Dictionary<string, object>
                                    { { "id", ncp.PlayItem.Id }, { "userId", Common.LoginedUser?.id } });
                            if (json["lrc"] != null)
                                return new PureLyricInfo
                                {
                                    PureLyrics = json["lrc"]?.ToString(),
                                    TrLyrics = null
                                };
                            */
                        return new PureLyricInfo
                        {
                            PureLyrics = "[00:00.000] 无歌词 请欣赏",
                            TrLyrics = null
                        };
                    try
                    {
                        return new PureLyricInfo
                        {
                            PureLyrics = json["lrc"]?["lyric"]?.ToString(),
                            TrLyrics = json["tlyric"]?["lyric"]?.ToString(),
                            NeteaseRomaji = json["romalrc"]?["lyric"]?.ToString()
                        };
                    }
                    catch (Exception)
                    {
                        //DEBUG
                    }
                }

                json.RemoveAll();
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
            insertList = insertList.Except(List,new HyPlayerItemComparer()).ToList();
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
            var ncp = new PlayItem
            {
                Type = ncSong.Type,
                //Bitrate = json["data"][0]["br"].ToObject<int>(),
                Tag = "在线",
                Album = ncSong.Album,
                Artist = ncSong.Artist,
                //SubExt = json["data"][0]["type"].ToString().ToLowerInvariant(),
                Id = ncSong.sid,
                Name = ncSong.songname,
                TrackId = ncSong.TrackId,
                CDName = ncSong.CDName,
                //Url = json["data"][0]["url"].ToString(),
                LengthInMilliseconds = ncSong.LengthInMilliseconds
                //Size = json["data"][0]["size"].ToString(),
                //md5 = json["data"][0]["md5"].ToString()
            };
            return LoadNcPlayItem(ncp);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        return null;
    }

    private static void AppendNcPlayItem(PlayItem ncp)
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
                var ncp = new PlayItem
                {
                    Type = ncSong.Type,
                    Tag = "在线",
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
                    var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object>
                            { { "ids", sourceId.Substring(2, sourceId.Length - 2) } });
                    _ = AppendNcSong(NCSong.CreateFromJson(json["songs"]?[0]));
                    json.RemoveAll();
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
            var j1 = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.ArtistTopSong,
                new Dictionary<string, object> { { "id", id } });


            var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.SongDetail,
                new Dictionary<string, object>
                {
                    ["ids"] = string.Join(",",
                        (j1["songs"] ?? new JArray()).ToList().Select(t => t["id"]))
                },
                false);
            var idx = 0;
            var list = new List<NCSong>();
            if (json["songs"] != null)
                foreach (var jToken in json["songs"])
                {
                    var ncSong = NCSong.CreateFromJson(jToken);
                    ncSong.IsAvailable = json["privileges"]?[idx]?["st"]?.ToString() == "0";
                    ncSong.Order = idx++;
                    list.Add(ncSong);
                }

            list.RemoveAll(t => t == null);
            AppendNcSongs(list, false);
            list.Clear();
            json.RemoveAll();
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
            var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.Album,
                new Dictionary<string, object> { { "id", albumId } });

            var list = new List<NCSong>();
            foreach (var song in (json["songs"] ?? new JArray()).ToArray())
            {
                var ncSong = NCSong.CreateFromJson(song);
                list.Add(ncSong);
            }

            list.RemoveAll(t => t == null || !t.IsAvailable);
            AppendNcSongs(list, false);
            list.Clear();
            json.RemoveAll();
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
                    var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.DjProgram,
                        new Dictionary<string, object>
                        {
                            { "rid", radioId },
                            { "offset", page++ * 100 },
                            { "limit", 100 },
                            { "asc", asc }
                        });
                    hasMore = json["more"]?.ToObject<bool>();
                    if (json["programs"] != null)
                        AppendNcSongs(
                            json["programs"].Select(t => (NCSong)NCFmItem.CreateFromJson(t)).ToList(),
                            false);
                    json.RemoveAll();
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
            var json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
                new Dictionary<string, object> { { "id", playlistId } });

            var nowIndex = 0;
            var trackIds = (json["playlist"]?["trackIds"] ?? new JArray()).Select(t => (int)t["id"])
                .ToList();
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    json = await Common.ncapi?.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object> { ["ids"] = string.Join(",", nowIds) });
                    nowIndex++;
                    var i = 0;
                    var ncSongs = (json["songs"] ?? new JArray()).Select(t =>
                    {
                        if (json["privileges"] == null) return null;
                        if (json["privileges"].ToList()[i++]["st"]?.ToString() == "0")
                            return NCSong.CreateFromJson(t);

                        return null;
                    }).ToList();
                    ncSongs.RemoveAll(t => t == null);
                    AppendNcSongs(ncSongs, false);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }
            }

            json.RemoveAll();
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
                    Tag = sf.Provider.DisplayName,
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
            Tag = sf.Provider.DisplayName
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
                if (Utilities.HasKana(lyricItem.LyricLine.CurrentLyric))
                    lyricItem.Romaji =
                        await Common.KawazuConv.Convert(lyricItem.LyricLine.CurrentLyric, To.Romaji, Mode.Separated);
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