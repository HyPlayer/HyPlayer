#region

using ALRC.Converters;
using ALRC.Converters.Enhancers;
using HyPlayer.Classes;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.Classes.LyricParser.Implementation;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Kawazu;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.Xaml.Media;
using Buffer = Windows.Storage.Streams.Buffer;
using File = TagLib.File;
using LrcConverter = ALRC.Converters.LrcConverter;
using Timer = System.Timers.Timer;

#endregion

namespace HyPlayer.HyPlayControl;

public static class HyPlayList
{
    public delegate void LoginDoneEvent();

    public delegate void LyricChangeEvent();

    public delegate void LyricColorChangeEvent();

    public delegate void ManualSeekEvent(TimeSpan position);

    public delegate void LyricLoadedEvent();

    public delegate void MediaEndEvent(HyPlayItem hpi);

    //public delegate void PlayItemAddEvent(HyPlayItem playItem);
    //public static event PlayItemAddEvent OnPlayItemAdd; //此方法因为效率原因废弃
    public delegate void PauseEvent();

    public delegate void PlayEvent();


    /********        事件        ********/
    public delegate void PlayItemChangeEvent(HyPlayItem playItem);

    public delegate void PlayListAddDoneEvent(bool isShuffleTrigger = false);

    public delegate void PlayModeChangedEvent(PlayMode mode);

    public delegate void PlayPositionChangeEvent(TimeSpan position);

    public delegate void SongBufferEndEvent();

    public delegate void SongBufferStartEvent();

    public delegate void SongMoveNextEvent();

    public delegate void SongRemoveAllEvent();

    public delegate void TimerTicked();

    public delegate void VolumeChangeEvent(double newVolume);

    public delegate void SongLikeStatusChanged(bool isLiked);

    public delegate void SongCoverChanged(HyPlayItem playItem);

    public static int NowPlaying = -1;
    private static readonly Timer SecTimer = new(1000); // 公用秒表
    public static readonly List<HyPlayItem> List = new();
    public static readonly List<int> ShuffleList = new();
    public static int ShufflingIndex = -1;
    public static HyLyricInfo HyLyricInfo = new();
    public static TimeSpan LyricOffset = TimeSpan.Zero;
    public static PropertySet AudioEffectsProperties = new PropertySet();
    private static CancellationTokenSource _mediaSourceCancellationTokenSource = new();
    private static SemaphoreSlim SeekerLock = new SemaphoreSlim(1);

    /********        API        ********/
    public static AudioGraphPlayer Player = Locator.Instance.GetService<AudioGraphPlayer>();
    public static FadeManager FadeManager = new FadeManager(Player);
    public static BackgroundDownloader Downloader = new();
    public static SystemMediaTransportControls MediaSystemControls;
    private static SystemMediaTransportControlsDisplayUpdater _controlsDisplayUpdater;
    private static Dictionary<HyPlayItem, DownloadOperation> DownloadOperations = new();
    private static Dictionary<DownloadOperation, HyPlayItem> DownloadOperationsReverseDirectory = new();
#nullable enable
    public static InMemoryRandomAccessStream? CoverStream;
    public static RandomAccessStreamReference? CoverStreamReference;
#nullable restore

    private static readonly IProgress<DownloadOperation> DefaultProgressCallback = new Progress<DownloadOperation>(ProgressCallback);

    // 常量定义
    private const string NCM_FILE_EXTENSION = ".ncm";
    private const string CACHE_FILE_NAME_FORMAT = "{0}.{1}";
    private const string SONG_URL_CACHE_KEY_FORMAT = "{0}_{1}";
    private const int SONG_URL_CACHE_MINUTES = 20;

    public static int LyricPos;

    public static string PlaySourceId;


    private static double _playerOutgoingVolume;
    public static double PlayerOutgoingVolume
    {
        get => _playerOutgoingVolume;
        set
        {
            _playerOutgoingVolume = value;
            Common.Setting.Volume = (int)(value * 100);
            OnVolumeChange?.Invoke(_playerOutgoingVolume);
            Player.SetOutputVolume(value);
        }
    }


    /*********        基本       ********/
    public static PlayMode NowPlayType
    {
        private set
        {
            Common.Setting.songRollType = (int)value;
            OnPlayModeChanged?.Invoke(value);
        }

        get => (PlayMode)Common.Setting.songRollType;
    }

    public static bool IsPlaying => Player.GlobalPlaybackStatus == PlaybackStatus.Playing;

    public static StorageFile NowPlayingStorageFile { get; private set; }


    public static HyPlayItem NowPlayingItem
    {
        get
        {
            if (Player.PrimaryPlaybackSource is AudioGraphPlaybackSource source && source.PlaybackSource.IsOpen)
            {
                return source.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
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
    public static event PlayModeChangedEvent OnPlayModeChanged;

    public static event LyricLoadedEvent OnLyricLoaded;

    public static event LyricChangeEvent OnLyricChange;
    public static event LyricColorChangeEvent OnLyricColorChange;
    public static event ManualSeekEvent OnManualSeek;

    public static event MediaEndEvent OnMediaEnd;

    public static event LyricChangeEvent OnSongMoveNext;

    public static event LoginDoneEvent OnLoginDone;

    public static event TimerTicked OnTimerTicked;

    public static event SongRemoveAllEvent OnSongRemoveAll;

    public static event SongLikeStatusChanged OnSongLikeStatusChange;

    public static event SongCoverChanged OnSongCoverChanged;

    public static async void InitializeHyPlaylist()
    {
        try
        {
            if (!Player.PlayerCreated)
            {
                await Player.InitializePlayer(new AudioGraphAudioSetting()
                {
                    DefaultDeviceId = Common.Setting.AudioRenderDevice,
                    OutputVolume = Common.Setting.Volume / 100d,
                    AutoFallback = true,
                    EnableFFTProcessing = Common.Setting.EnableFFT
                });
            }
            MediaSystemControls = SystemMediaTransportControls.GetForCurrentView();
            MediaSystemControls.PlaybackPositionChangeRequested += MediaSystemControls_PlaybackPositionChangeRequested;
            Player.SMTCManager = new SMTCManager(MediaSystemControls);
            _controlsDisplayUpdater = MediaSystemControls.DisplayUpdater;
            MediaSystemControls.IsPlayEnabled = true;
            MediaSystemControls.IsPauseEnabled = true;
            MediaSystemControls.IsNextEnabled = true;
            MediaSystemControls.IsPreviousEnabled = true;
            MediaSystemControls.IsEnabled = true;
            MediaSystemControls.ButtonPressed += SystemControls_ButtonPressed;
            MediaSystemControls.PlaybackStatus = MediaPlaybackStatus.Closed;
            Player.OnTrackReachesEnd += Player_MediaEnded;
            Player.OnGlobalPlaybackStatusChanged += Player_CurrentStateChanged;
            Player.OnPositionChanged += PlaybackSession_PositionChanged;

            Player.OnPrimaryPlaybackSourceChanged += Player_SourceChanged;
            SecTimer.Elapsed += (sender, args) => _ = Common.Invoke(() => OnTimerTicked?.Invoke());
            SecTimer.Start();
            if (Common.Setting.highPreciseLyricTimer)
            {
                highTimer.Elapsed += (_, _) => { LoadLyricChange(); };
                highTimer.Start();
            }

            HistoryManagement.InitializeHistoryTrack();
            Common.IsInFm = false;
        }
        catch (Exception e)
        {
            Common.AddToTeachingTipLists("初始化播放器失败", e.Message);
        }
    }

    public async static void Seek(TimeSpan targetTimeSpan)
    {
        try
        {
            await SeekerLock.WaitAsync();
            if (Player.PrimaryPlaybackSource is null) return;
            Player.SeekPlaybackSource(targetTimeSpan, Player.PrimaryPlaybackSource);
            OnManualSeek?.Invoke(targetTimeSpan);
            await Task.Delay(500);
        }
        finally
        {
            SeekerLock.Release();
        }
    }

    public static void FireLyricColorChangeEvent()
    {
        OnLyricColorChange?.Invoke();
    }

    public static void MediaSystemControls_PlaybackPositionChangeRequested(SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        Seek(args.RequestedPlaybackPosition);
    }


    public static void ChangePlayMode(PlayMode playMode)
    {
        NowPlayType = playMode;
        if (playMode == PlayMode.Shuffled)
        {
            if (Common.Setting.shuffleNoRepeating)
            {
                CreateShufflePlayLists(NowPlayingItem.PlayItem.Id);
            }
            else
            {
                ShuffleList.Clear();
                ShuffleList.AddRange(Enumerable.Range(0, List.Count));
            }
        }
        else
        {
            ShuffleList.Clear();
        }

        OnPlayModeChanged?.Invoke(playMode);
    }

    public static void LoginDoneCall()
    {
        _ = Common.Invoke(() => { OnLoginDone?.Invoke(); });
    }


    private static void PlayerOnMediaFailed(string reason)
    {
        Common.ErrorMessageList.Add($"歌曲播放失败: {NowPlayingItem.PlayItem.Name}\n{reason}");
        Common.AddToTeachingTipLists($"播放失败 切到下一曲 \n 歌曲: {NowPlayingItem.PlayItem.Name}\n{reason}");
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

        var files = await fop.PickMultipleFilesAsync();
        if (files == null || files.Count == 0) return;

        var isFirstLoad = true;

        foreach (var file in files)
        {
            try
            {
                // 使用 Polly 重试策略处理文件访问
                await RetryPolicies.FileAccessPolicy.ExecuteAsync(async () =>
                {
                    var folder = await file.GetParentAsync();
                    if (folder != null)
                    {
                        if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(folder.Path.GetHashCode().ToString()))
                            StorageApplicationPermissions.FutureAccessList.AddOrReplace(folder.Path.GetHashCode().ToString(), folder);
                    }
                    else
                    {
                        if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(file.Path.GetHashCode().ToString()))
                            StorageApplicationPermissions.FutureAccessList.AddOrReplace(file.Path.GetHashCode().ToString(), file);
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
                                    Name = Info.musicName,
                                    InfoTag = file.Provider.DisplayName + " NCM"
                                }
                            };
                            hyitem.PlayItem.Artist = Info.artist.Select(t => new NCArtist
                            { name = t[0].ToString(), id = t[1].ToString() })
                                .ToList();

                            List.Add(hyitem);
                        }
                        else
                        {
                            throw new Exception("NCM 文件格式不正确");
                        }
                    }
                    else
                    {
                        await AppendStorageFile(file);
                    }
                });

                if (!isFirstLoad) continue;
                isFirstLoad = false;
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists($"加载文件 {file.Name} 失败", ex.Message);
            }
        }

        SongAppendDone();
        if (List.Count > 0)
        {
            SongMoveTo(List.LastOrDefault());
        }
    }


    private static async Task LoadLocalFile(HyPlayItem targetItem, CancellationToken ctk)
    {
        // 使用 Polly 重试策略优化本地文件加载
        await RetryPolicies.LocalFileLoadPolicy.ExecuteAsync(async () =>
        {
            ctk.ThrowIfCancellationRequested();

            // 此处可以改进
            if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
                throw new ArgumentException("不支持的文件类型");

            if (targetItem.PlayItem.DontSetLocalStorageFile != null)
            {
                if (targetItem.ItemType != HyPlayItemType.LocalProgressive)
                {
                    NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
                }
                else
                {
                    NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
                    var item = await LoadStorageFile(targetItem.PlayItem.DontSetLocalStorageFile, ctk);
                    targetItem.ItemType = HyPlayItemType.Local;
                    targetItem.PlayItem = item.PlayItem;
                    targetItem.PlayItem.DontSetLocalStorageFile = NowPlayingStorageFile;
                }
            }
            else
            {
                NowPlayingStorageFile = await StorageFile.GetFileFromPathAsync(targetItem.PlayItem.Url);
            }
        });
    }

    public async static Task LoadNCMFile(HyPlayItem targetItem, CancellationToken ctk)
    {
        // 使用 Polly 重试策略优化 NCM 文件解析
        await RetryPolicies.NcmFileLoadPolicy.ExecuteAsync(async () =>
        {
            ctk.ThrowIfCancellationRequested();

            // 脑残Music解析
            using var stream = await targetItem.PlayItem.DontSetLocalStorageFile.OpenStreamForReadAsync();
            if (!NCMFile.IsCorrectNCMFile(stream))
            {
                throw new Exception("NCM 文件格式不正确");
            }

            var info = NCMFile.GetNCMMusicInfo(stream);
            var coverArray = NCMFile.GetCoverByteArray(stream);
            var buffer = coverArray.AsBuffer();
            var oldCoverStream = CoverStream;
            CoverStream = null;
            CoverStreamReference = null;
            oldCoverStream?.Dispose();
            CoverStream = new InMemoryRandomAccessStream();
            await CoverStream.WriteAsync(buffer);
            CoverStreamReference = RandomAccessStreamReference.CreateFromStream(CoverStream);
            using var encStream = NCMFile.GetEncryptedStream(stream);
            encStream.Seek(0, SeekOrigin.Begin);
            var songDataStream = new InMemoryRandomAccessStream();
            var targetSongDataStream = songDataStream.AsStream();
            encStream.CopyTo(targetSongDataStream);
            targetItem.PlayItem.NcmPlayableStream = songDataStream;
            NowPlayingStorageFile = targetItem.PlayItem.DontSetLocalStorageFile;
            targetItem.PlayItem.NcmPlayableStreamMIMEType = MIMEHelper.GetNCMFileMimeType(info.format);
        });
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

        if (List.Count == 0) return;
        OnSongMoveNext?.Invoke();
        MoveSongPointer(true);
        _ = LoadMediaSource(List[NowPlaying], true);
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
        OnSongMoveNext?.Invoke();
        if (!Common.IsInFm && List.Count != 0)
        {
            _ = LoadMediaSource(List[NowPlaying], true);
        }
    }

    public static void SongMoveTo(HyPlayItem item)
    {
        if (!List.Contains(item)) return;
        var index = List.IndexOf(item);
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
            ShufflingIndex = ShuffleList.IndexOf(index);
        var currentPlayItem = NowPlayingItem;
        NowPlaying = index;
        if (currentPlayItem != item)
        {
            _ = LoadMediaSource(item, true);
            OnSongMoveNext?.Invoke();
        }
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
            _ = LoadMediaSource(List[NowPlaying]);
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
        if (resetPlaying)
        {
            Player.RemoveAllPlaybackSource();
            var songsToBeFree = List.Where(t => t.PlayItem.AudioGraphPlaybackSource != null).ToList();
            songsToBeFree.ForEach(t => t.PlayItem?.FreePlaybackResources());
        }
        List.Clear();
        NowPlaying = -1;
        OnSongRemoveAll?.Invoke();
        SongAppendDone();
    }

    public static async void LikeSong()
    {
        var isLiked = Common.LikedSongs.Contains(NowPlayingItem.PlayItem.Id);

        try
        {
            // 使用 Polly 重试策略优化红心操作
            await RetryPolicies.ApiCallPolicy.ExecuteAsync(async () =>
            {
                switch (NowPlayingItem.ItemType)
                {
                    case HyPlayItemType.Netease:
                        {
                            bool res = await Api.LikeSong(NowPlayingItem.PlayItem.Id, !isLiked);
                            if (res)
                            {
                                if (isLiked)
                                    Common.LikedSongs.Remove(NowPlayingItem.PlayItem.Id);
                                else
                                    Common.LikedSongs.Add(NowPlayingItem.PlayItem.Id);

                                _ = Common.Invoke(() => OnSongLikeStatusChange?.Invoke(!isLiked));
                            }
                            else
                            {
                                throw new Exception("红心操作失败");
                            }
                            break;
                        }
                    case HyPlayItemType.Radio:
                        // TODO: 待实现电台红心
                        Common.AddToTeachingTipLists("暂不支持红心电台歌曲", "将在后续版本中支持");
                        _ = Common.Invoke(() => OnSongLikeStatusChange?.Invoke(!isLiked));
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("红心操作失败", ex.Message);
        }
    }
    /********        相关事件处理        ********/

    private static void SystemControls_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                //Player.Play();
                Player.PlayAll();
                break;
            case SystemMediaTransportControlsButton.Pause:
                //Player.Pause();
                Player.PauseAll();
                break;
            case SystemMediaTransportControlsButton.Previous:
                //SongMovePrevious();
                SongMovePrevious();
                break;
            case SystemMediaTransportControlsButton.Next:
                //SongMoveNext();
                SongMoveNext();
                break;
        }
    }

    public static void MoveSongPointer(bool realNext = false)
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

    private static void Player_MediaEnded(IPlaybackSource playbackSource)
    {
        //当播放结束时,此时你应当进行切歌操作
        //不过在此之前还是把订阅了的时间给返回回去吧
        var source = playbackSource as AudioGraphPlaybackSource;
        if (source == null) return;
        var item = source.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
        OnMediaEnd?.Invoke(item);
        if (NowPlayType != PlayMode.SinglePlay && !Common.Setting.CrossFade)
        {
            MoveSongPointer();
            //然后尝试加载下一首歌
            if (List.Count != 0)
            {
                _ = LoadMediaSource(List[NowPlaying]);
            }
        }
        else if (NowPlayType == PlayMode.SinglePlay || List.Count <= 1)
        {
            Seek(TimeSpan.Zero);
        }
        _ = LastFMManager.Scrobble(item);
    }
    public static double GetAudioGainMultiplier(double audioGainValue)
    {
        var gainValue = Math.Pow(10, audioGainValue / 20);
        return gainValue;
    }

    private static async Task<(string, long)> GetNowPlayingUrl(HyPlayItem targetItem, CancellationToken ctk)
    {
        var playUrl = targetItem.PlayItem.Url;
        var size = targetItem.PlayItem.Size;

        // 对了,先看看是否要刷新播放链接
        if ((string.IsNullOrEmpty(targetItem.PlayItem.Url) ||
             Common.Setting.songUrlLazyGet) && targetItem.PlayItem.Id != "-1")
        {
            // 使用 Polly 重试策略优化 URL 获取
            var songResult = await RetryPolicies.UrlFetchPolicy.ExecuteAsync(async () =>
            {
                var result = await SimpleCacher.GetOrCreateCacheAsync(
                    CacheType.SongUrl,
                    string.Format(SONG_URL_CACHE_KEY_FORMAT, targetItem.PlayItem.Id, Common.Setting.audioRate),
                    async () =>
                    {
                        ctk.ThrowIfCancellationRequested();

                        var songRequest = new SongUrlRequest
                        {
                            Level = Common.Setting.audioRate,
                            Id = targetItem.PlayItem.Id
                        };

                        var songRes = await Common.NeteaseAPI!.RequestAsync(
                            NeteaseApis.SongUrlApi, songRequest);

                        if (songRes.IsError)
                        {
                            throw new Exception("API 请求失败");
                        }

                        return songRes.Value;
                    },
                    TimeSpan.FromMinutes(SONG_URL_CACHE_MINUTES),
                    cancellationToken: ctk);

                if (result == null)
                {
                    throw new Exception("下载链接获取失败");
                }

                return result;
            });

            if (songResult?.SongUrls?[0].Code == 200)
            {
                if (songResult.SongUrls[0].FreeTrialInfo is not null && Common.Setting.jumpVipSongPlaying)
                {
                    throw new Exception("当前歌曲为 VIP 试听, 已自动跳过");
                }

                playUrl = songResult.SongUrls[0].Url;
                size = songResult.SongUrls[0].Size;
                if (Common.Setting.UseHttpWhenGettingSongs && playUrl.Contains("https://"))
                {
                    playUrl = playUrl.Replace("https://", "http://");
                }

                UpdatePlayItemQualityInfo(targetItem, songResult.SongUrls[0]);
            }
            else
            {
                throw new Exception("下载链接获取失败");
            }
        }

        return (playUrl, size);
    }

    private static void UpdatePlayItemQualityInfo(HyPlayItem targetItem, SongUrlResponse.SongUrlItem urlInfo)
    {
        if (urlInfo == null) return;

        var tag = urlInfo.Level switch
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
        targetItem.PlayItem.Size = urlInfo.Size;
        targetItem.PlayItem.SubExt = urlInfo.Type?.ToLowerInvariant();

        var volume = GetAudioGainMultiplier(urlInfo.Gain ?? 0f);
        targetItem.PlayItem.Volume = volume;

        UpdatePlayBarQualityDisplay(targetItem.PlayItem.QualityTag);
    }

    private static void UpdatePlayBarQualityDisplay(string qualityTag)
    {
        _ = Common.Invoke(() =>
        {
            Common.BarPlayBar.TbSongTag.Text = qualityTag;
            if (qualityTag.Length > 2)
            {
                var backgroundbrush = new LinearGradientBrush();
                backgroundbrush.StartPoint = new Windows.Foundation.Point(0, 0);
                backgroundbrush.EndPoint = new Windows.Foundation.Point(1, 1);

                backgroundbrush.GradientStops.Add(new GradientStop
                { Offset = 0, Color = Color.FromArgb(255, 251, 251, 206) });
                backgroundbrush.GradientStops.Add(new GradientStop
                { Offset = 1, Color = Color.FromArgb(255, 223, 155, 28) });

                Common.BarPlayBar.SongInfoTag.Background = backgroundbrush;
                Common.BarPlayBar.SongInfoTag.BorderBrush =
                    new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                Common.BarPlayBar.TbSongTag.Foreground =
                    new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
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

    /// <summary>
    /// 安全地清理下载操作资源
    /// </summary>
    private static void CleanupDownloadOperation(HyPlayItem targetItem)
    {
        if (DownloadOperations.TryGetValue(targetItem, out var operation))
        {
            DownloadOperations.Remove(targetItem);
            DownloadOperationsReverseDirectory.Remove(operation);
        }
    }

    /// <summary>
    /// 验证播放项是否有效
    /// </summary>
    private static bool ValidatePlayItem(HyPlayItem targetItem)
    {
        return targetItem?.PlayItem != null && !string.IsNullOrEmpty(targetItem.PlayItem.Name);
    }

    /// <summary>
    /// 处理播放失败的通用方法
    /// </summary>
    private static void HandlePlaybackFailure(string message, Exception exception = null)
    {
        var errorMessage = $"{message}";
        if (exception != null)
        {
            errorMessage += $"\n{exception.Message}";
        }

        Common.ErrorMessageList.Add(errorMessage);
        Common.AddToTeachingTipLists("播放失败", errorMessage);
        SongMoveNext();
    }

    public static async Task LoadMediaSource(HyPlayItem targetItem, bool setAsPrimary = false, bool autoPlay = true)
    {
        _mediaSourceCancellationTokenSource.Cancel();
        _mediaSourceCancellationTokenSource = new CancellationTokenSource();
        var ctk = _mediaSourceCancellationTokenSource.Token;
        try
        {
            // 使用 Polly 重试策略优化核心逻辑
            await RetryPolicies.MediaSourceLoadPolicy.ExecuteAsync(async () =>
            {
                if (ctk.IsCancellationRequested) return;
                if (targetItem.PlayItem?.Name == null)
                {
                    MoveSongPointer();
                    return;
                }

                if (Player.PrimaryPlaybackSource != null && !Common.Setting.CrossFade && !FadeManager.FadeProcessing)
                {
                    var primaryPlaybackSource = Player.PrimaryPlaybackSource as AudioGraphPlaybackSource;
                    Player.PausePlaybackSource(primaryPlaybackSource);
                    Player.DisconnectPlaybackSource(Player.PrimaryPlaybackSource);
                    if (primaryPlaybackSource != null)
                    {
                        var item = primaryPlaybackSource.PlaybackSource.CustomProperties["nowPlayingItem"] as HyPlayItem;
                        item?.PlayItem?.FreePlaybackResources();
                    }
                }

                var mediaSource = await CreateMediaSourceAsync(targetItem, ctk);
                ctk.ThrowIfCancellationRequested();
                mediaSource?.CustomProperties.Add("nowPlayingItem", targetItem);
                MediaSystemControls.IsEnabled = true;

                if (!Common.Setting.enableCache) await mediaSource.OpenAsync();

                UpdatePlayItemDuration(targetItem, mediaSource);

                var playbackSource = new AudioGraphPlaybackSource(mediaSource);
                targetItem.PlayItem.AudioGraphPlaybackSource = playbackSource;

                var targetVolume = Common.Setting.EnableAudioGain ? targetItem.PlayItem.Volume : 1d;
                if (Common.Setting.CrossFade && FadeManager.FadeProcessing)
                {
                    targetVolume = 0;
                }

                var options = new PlaybackOptions() { SetAsPrimarySource = setAsPrimary, AutoPlay = autoPlay, Volume = targetVolume };
                await Player.ConnectPlaybackSourceAsync(playbackSource, options);
            });
        }
        catch (Exception e)
        {
            PlayerOnMediaFailed(e.Message);
        }
    }

    private static async Task<MediaSource> CreateMediaSourceAsync(HyPlayItem targetItem, CancellationToken ctk)
    {
        return targetItem.ItemType switch
        {
            HyPlayItemType.Netease or HyPlayItemType.Radio => await CreateNeteaseMediaSourceAsync(targetItem, ctk),
            HyPlayItemType.Local or HyPlayItemType.LocalProgressive => await CreateLocalMediaSourceAsync(targetItem, ctk),
            _ => throw new NotSupportedException($"Unsupported item type: {targetItem.ItemType}")
        };
    }

    private static async Task<MediaSource> CreateNeteaseMediaSourceAsync(HyPlayItem targetItem, CancellationToken ctk)
    {
        if (targetItem.PlayItem.IsLocalFile)
        {
            return await CreateLocalFileMediaSourceAsync(targetItem, ctk);
        }

        if (Common.Setting.enableCache)
        {
            return await CreateCachedMediaSourceAsync(targetItem, ctk);
        }

        var playUrl = await GetNowPlayingUrlWithRetry(targetItem, ctk);
        return MediaSource.CreateFromUri(new Uri(playUrl.Item1));
    }

    private static async Task<MediaSource> CreateLocalFileMediaSourceAsync(HyPlayItem targetItem, CancellationToken ctk)
    {
        if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
        {
            await LoadNCMFile(targetItem, ctk);
            return MediaSource.CreateFromStream(targetItem.PlayItem.NcmPlayableStream, targetItem.PlayItem.NcmPlayableStreamMIMEType);
        }
        else
        {
            await LoadLocalFile(targetItem, ctk);
            return MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
        }
    }

    private static async Task<MediaSource> CreateCachedMediaSourceAsync(HyPlayItem targetItem, CancellationToken ctk = default)
    {
        var playUrlRes = await GetNowPlayingUrlWithRetry(targetItem, ctk);

        var cacheFile = await GetCacheFileAsync(targetItem, ctk);
        if (cacheFile != null)
        {
            return MediaSource.CreateFromStorageFile(cacheFile);
        }

        // 缓存文件无效，重新下载
        var rst = await RetryPolicies.FastFailPolicy.ExecuteAndCaptureAsync(async () => await DownloadAndCreateMediaSourceAsync(targetItem, playUrlRes.Item1, playUrlRes.Item2, ctk));
        return rst.Result;

    }

    private static async Task<StorageFile> GetCacheFileAsync(HyPlayItem targetItem, CancellationToken ctk = default)
    {
        try
        {
            var cacheFolder = await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir);
            var fileName = string.Format(CACHE_FILE_NAME_FORMAT, targetItem.PlayItem.Id, targetItem.PlayItem?.SubExt);
            var cacheFile = await cacheFolder.GetFileAsync(fileName);

            var properties = await cacheFile.GetBasicPropertiesAsync();
            if (properties.Size == (ulong)(targetItem.PlayItem?.Size ?? -1))
            {
                return cacheFile;
            }
            else
            {
                await cacheFile.DeleteAsync();
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static async Task<MediaSource> DownloadAndCreateMediaSourceAsync(HyPlayItem targetItem, string playUrl, long size, CancellationToken ctk)
    {
        if (string.IsNullOrEmpty(playUrl))
            throw new Exception("Play URL is null");

        // 检查是否已存在下载操作
        if (DownloadOperations.TryGetValue(targetItem, out var existingOperation))
        {
            return MediaSource.CreateFromDownloadOperation(existingOperation);
        }

        var destinationFolder = await StorageFolder.GetFolderFromPathAsync(Common.Setting.cacheDir);
        var fileName = string.Format(CACHE_FILE_NAME_FORMAT, targetItem.PlayItem.Id, targetItem.PlayItem?.SubExt);
        var destinationFile = await destinationFolder.CreateFileAsync(fileName, CreationCollisionOption.OpenIfExists);
        var operation = Downloader.CreateDownload(new Uri(playUrl), destinationFile);
        //operation.IsRandomAccessRequired = true;
        DownloadOperations[targetItem] = operation;
        DownloadOperationsReverseDirectory[operation] = targetItem;
        await operation.StartAsync().AsTask(ctk);
        _ = operation.AttachAsync().AsTask(ctk, DefaultProgressCallback);
        var mediaSource = MediaSource.CreateFromDownloadOperation(operation);
        return mediaSource;
    }

    private static async Task<MediaSource> CreateLocalMediaSourceAsync(HyPlayItem targetItem, CancellationToken ctk)
    {
        if (targetItem.PlayItem.DontSetLocalStorageFile == null && targetItem.PlayItem.Url != null)
        {
            targetItem.PlayItem.DontSetLocalStorageFile =
                await StorageFile.GetFileFromPathAsync(targetItem.PlayItem.Url);
        }

        if (targetItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
        {
            await LoadNCMFile(targetItem, ctk);
            return MediaSource.CreateFromStream(targetItem.PlayItem.NcmPlayableStream, targetItem.PlayItem.NcmPlayableStreamMIMEType);
        }
        else
        {
            await LoadLocalFile(targetItem, ctk);
            return MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
        }
    }

    private static async Task<(string, long)> GetNowPlayingUrlWithRetry(HyPlayItem targetItem, CancellationToken ctk)
    {
        return await RetryPolicies.UrlFetchPolicy.ExecuteAsync(async () =>
            await GetNowPlayingUrl(targetItem, ctk));
    }

    private static void UpdatePlayItemDuration(HyPlayItem targetItem, MediaSource mediaSource)
    {
        var duration = mediaSource.Duration?.TotalMilliseconds;
        if (duration != null && targetItem.PlayItem.LengthInMilliseconds != duration.Value)
        {
            targetItem.PlayItem.LengthInMilliseconds = duration.Value;
        }
    }
    private static void ProgressCallback(DownloadOperation obj)
    {
        if (obj.Progress.TotalBytesToReceive == obj.Progress.BytesReceived && obj.CurrentWebErrorStatus == null)
        {
            var result = DownloadOperationsReverseDirectory.TryGetValue(obj, out var item);
            if (result)
            {
                DownloadOperationsReverseDirectory.Remove(obj);
                DownloadOperations.Remove(item);
            }
            return;
        }
    }
    public static void Player_SourceChanged(IPlaybackSource source)
    {
        if (List.Count <= NowPlaying) return;
        if (NowPlayingItem.PlayItem == null || source == null)
        {
            return;
        }

        var playItemWhenRequested = NowPlayingItem;
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
            if (NowPlayingItem == playItemWhenRequested)
            {
                NotifyPlayItemChanged(playItemWhenRequested);
            }

            // 图片加载放在之后
            if (!Common.Setting.noImage && NowPlayingItem == playItemWhenRequested)
            {
                _ = RefreshAlbumCover(playItemWhenRequested).ContinueWith(async (_) =>
                {

                    if ((playItemWhenRequested == NowPlayingItem) && !Common.Setting.noImage)
                    {
                        OnSongCoverChanged?.Invoke(playItemWhenRequested);
                    }

                    //更新磁贴
                    if (playItemWhenRequested == NowPlayingItem)
                    {
                        using var stream = CoverStream.CloneStream();
                        await RefreshTile(playItemWhenRequested, playItemWhenRequested, stream);
                    }

                    if (playItemWhenRequested == NowPlayingItem)
                    {
                        // RASR 罪大恶极，害的磁贴怨声载道
                        _controlsDisplayUpdater.Thumbnail = CoverStreamReference;
                        _controlsDisplayUpdater.Update();
                    }
                });
            }



            if (NowPlayingItem == playItemWhenRequested)
            {
                //加载歌词
                _ = LoadLyrics(playItemWhenRequested);
            }
            if (Common.Setting.UpdateLastFMNowPlaying)
            {
                _ = LastFMManager.UpdateNowPlaying(playItemWhenRequested);
            }
            //这里要判断这么多次的原因在于如果只判断一次的话，后面如果切歌是无法知晓的。所以只能用这个蠢方法
        }
    }

    public static async Task RefreshAlbumCover(HyPlayItem playItem)
    {
        try
        {
            if (playItem.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
            {
                if (NowPlayingStorageFile != null)
                {
                    if (!Common.Setting.useTaglibPicture || playItem.PlayItem.LocalFileTag is null ||
                        playItem.PlayItem.LocalFileTag.Pictures.Length == 0)
                    {
                        if (NowPlayingStorageFile != null)
                        {
                            using var thumbnail =
                                await NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.MusicView, 3000);
                            var buffer = new Buffer((uint)thumbnail.Size);
                            await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                            if (playItem == NowPlayingItem)
                            {
                                var oldCoverStream = CoverStream;
                                CoverStream = null;
                                CoverStreamReference = null;
                                oldCoverStream?.Dispose();
                                CoverStream = new InMemoryRandomAccessStream();
                                await CoverStream.WriteAsync(buffer);
                                CoverStreamReference = RandomAccessStreamReference.CreateFromStream(CoverStream);
                            }
                        }
                        else
                        {
                            var file = await StorageFile.GetFileFromPathAsync("/Assets/icon.png");
                            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 3000);
                            var buffer = new Buffer((uint)thumbnail.Size);
                            await thumbnail.ReadAsync(buffer, (uint)thumbnail.Size, InputStreamOptions.None);
                            if (playItem == NowPlayingItem)
                            {
                                var oldCoverStream = CoverStream;
                                CoverStream = null;
                                CoverStreamReference = null;
                                oldCoverStream?.Dispose();
                                CoverStream = new InMemoryRandomAccessStream();
                                await CoverStream.WriteAsync(buffer);
                                CoverStreamReference = RandomAccessStreamReference.CreateFromStream(CoverStream);
                            }
                        }
                    }
                    else
                    {
                        var bufferByte = playItem.PlayItem.LocalFileTag.Pictures[0].Data.Data;
                        var buffer = bufferByte.AsBuffer();
                        if (playItem == NowPlayingItem)
                        {
                            var oldCoverStream = CoverStream;
                            CoverStream = null;
                            CoverStreamReference = null;
                            oldCoverStream?.Dispose();
                            CoverStream = new InMemoryRandomAccessStream();
                            await CoverStream.WriteAsync(buffer);
                            CoverStreamReference = RandomAccessStreamReference.CreateFromStream(CoverStream);
                        }
                    }
                }
            }
            else
            {
                using var result =
                    await Common.HttpClient.GetAsync(new Uri(playItem.PlayItem.Album.cover + "?param=" + StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
                if (!result.IsSuccessStatusCode)
                {
                    throw new Exception("更新SMTC图片时发生异常");
                }

                var buffer = (await result.Content.ReadAsByteArrayAsync()).AsBuffer();
                if (playItem == NowPlayingItem)
                {
                    var oldCoverStream = CoverStream;
                    CoverStream = null;
                    CoverStreamReference = null;
                    oldCoverStream?.Dispose();
                    CoverStream = new InMemoryRandomAccessStream();
                    await CoverStream.WriteAsync(buffer);
                    CoverStreamReference = RandomAccessStreamReference.CreateFromStream(CoverStream);
                }
            }
        }
        catch
        {
            //ignore
        }
    }

    public static void NotifyPlayItemChanged(HyPlayItem targetItem)
    {
        OnPlayItemChange?.Invoke(targetItem);
    }

    public static async Task RefreshTile(HyPlayItem itemWhenRequested, HyPlayItem targetItem, IRandomAccessStream coverStream)
    {
        try
        {
            if (targetItem?.PlayItem == null || !Common.Setting.enableTile) return;
            string fileName = targetItem.PlayItem.IsLocalFile
                ? null
                : targetItem.PlayItem.Album.id;
            bool coverStreamIsAvailable = coverStream.Size != 0 && fileName != null && fileName != "0" &&
                                          itemWhenRequested == NowPlayingItem;
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
                    if (NowPlayingItem != itemWhenRequested)
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

    private static void PlaybackSession_PositionChanged(TimeSpan position)
    {
        OnPlayPositionChange?.Invoke(position);
        LoadLyricChange();
    }


    static Timer highTimer = new Timer(10);


    private static void LoadLyricChange()
    {
        if (Player.PrimaryAudioInputNode == null) return;
        if (HyLyricInfo.Lyrics.Count == 0) return;
        if (LyricPos >= HyLyricInfo.Lyrics.Count || LyricPos < 0) LyricPos = 0;
        var changed = false;
        var realPos = Player.PrimaryAudioInputNode.Position - LyricOffset;
        if (HyLyricInfo.Lyrics[LyricPos].LyricLine.StartTime > realPos) //当感知到进度回溯时执行
        {
            LyricPos = HyLyricInfo.Lyrics.FindLastIndex(t => t.LyricLine.StartTime <= realPos) - 1;
            if (LyricPos == -2) LyricPos = -1;
            changed = true;
        }

        try
        {
            if (LyricPos == 0 && HyLyricInfo.Lyrics.Count != 1) changed = false;
            while (HyLyricInfo.Lyrics.Count > LyricPos + 1 &&
                   HyLyricInfo.Lyrics[LyricPos + 1].LyricLine.StartTime <= realPos) //正常的滚歌词
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

    private static void Player_CurrentStateChanged(PlaybackStatus status)
    {
        //先通知 SystemMediaTransportControls

        if (status == PlaybackStatus.Playing)
            OnPlay?.Invoke();
        else if (status == PlaybackStatus.Paused)
            OnPause?.Invoke();
    }
    private static async Task LoadLyrics(HyPlayItem hpi, CancellationToken ctk = default)
    {
        var cache = await SimpleCacher.GetOrCreateCacheAsync(CacheType.HyLyricInfo, hpi.PlayItem.Id, () => Task.FromResult<HyLyricInfo>(null), cancellationToken: ctk);
        if (cache is not null)
        {
            HyLyricInfo = cache;
            OnLyricLoaded?.Invoke();
            OnLyricChange?.Invoke();
            return;
        }
        var pureLyricInfo = new PureLyricInfo();
        var unionTranslation = false;
        switch (hpi.ItemType)
        {
            case HyPlayItemType.Netease:
                pureLyricInfo = await LoadNcLyric(hpi, ctk);
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
            HyLyricInfo.Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics, unionTranslation);
        }
        else
        {
            HyLyricInfo.Lyrics = Utils.ConvertKaraok(pureLyricInfo);
        }

        if (HyLyricInfo.Lyrics.Count == 0)
        {
            if (Common.Setting.showComposerInLyric)
                HyLyricInfo.Lyrics.Add(new SongLyric
                {
                    LyricLine = new LrcLyricsLine(NowPlayingItem.PlayItem.ArtistString, TimeSpan.Zero)
                });
        }
        else
        {
            if (pureLyricInfo is not KaraokLyricInfo karaoke)
                Utils.ConvertTranslation(pureLyricInfo.TrLyrics, HyLyricInfo.Lyrics);
            else Utils.ConvertYrcTranslation(karaoke, HyLyricInfo.Lyrics);
            await Utils.ConvertRomaji(pureLyricInfo, HyLyricInfo.Lyrics);

            if (HyLyricInfo.Lyrics.Count != 0 && HyLyricInfo.Lyrics[0].LyricLine.StartTime != TimeSpan.Zero)
                HyLyricInfo.Lyrics.Insert(0,
                    new SongLyric { LyricLine = new LrcLyricsLine(string.Empty, TimeSpan.Zero) });
        }

        HyLyricInfo.LyricMetadata = pureLyricInfo.LyricMetadata;
        HyLyricInfo.SongMetadata = pureLyricInfo.SongMetadata;
        HyLyricInfo.PureLyricInfo = pureLyricInfo;

        LyricPos = 0;

        OnLyricLoaded?.Invoke();
        OnLyricChange?.Invoke();
        if (hpi.ItemType == HyPlayItemType.Netease)
        {
            _ = SimpleCacher.GetOrCreateCacheAsync(CacheType.HyLyricInfo, hpi.PlayItem.Id, () => Task.FromResult(HyLyricInfo), cancellationToken: CancellationToken.None);
        }

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
                var trLrc = lrcTranslationConverter.Extract(alrc);
                HyALRCLyricInfo ttmlLyric = new HyALRCLyricInfo()
                {
                    PureLyrics = lrc,
                    TrLyrics = trLrc,
                    ALRC = alrc,
                    LyricMetadata =
                    [
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
                            ActionUri =
                                $"https://github.com/Steve-xmh/amll-ttml-db/blob/main/ncm-lyrics/{hpi.PlayItem.Id}.ttml"
                        }
                    ],
                    SongMetadata = []
                };

                HyLyricInfo.Lyrics = Utils.ConvertPureLyric(ttmlLyric.PureLyrics, true);
                Utils.ConvertTranslation(ttmlLyric.TrLyrics, HyLyricInfo.Lyrics);
                HyLyricInfo.LyricMetadata = ttmlLyric.LyricMetadata;
                HyLyricInfo.SongMetadata = ttmlLyric.SongMetadata;
                HyLyricInfo.PureLyricInfo = ttmlLyric;

                OnLyricLoaded?.Invoke();
                OnLyricChange?.Invoke();
                _ = SimpleCacher.GetOrCreateCacheAsync(CacheType.HyLyricInfo, hpi.PlayItem.Id, () => Task.FromResult(HyLyricInfo), cancellationToken: CancellationToken.None);
            }
        }
        catch
        {
            // ignore
        }
    }


    private static async Task<PureLyricInfo> LoadNcLyric(HyPlayItem ncp, CancellationToken cancellationToken = default)
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
                var lyricResult = await SimpleCacher.GetOrCreateCacheAsync(CacheType.LyricApi, ncp.PlayItem.Id,
                    async () =>
                    {
                        var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.LyricApi, lyricRequest);
                        if (resp.IsError)
                        {
                            Common.AddToTeachingTipLists("获取歌词失败", resp.Error?.Message);
                            return null;
                        }

                        return resp.Value;
                    }, cancellationToken: cancellationToken);
                string lrc, romaji, karaoklrc, translrc, yrromaji, yrtranslrc;
                if (lyricResult is null)
                {
                    return new PureLyricInfo
                    {
                        PureLyrics = "[00:00.000] 歌词获取失败",
                        TrLyrics = null
                    };
                }

                if (lyricResult?.Lyric is null && lyricResult?.YunLyric is null)
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


                if (lyricResult?.YunLyric?.Lyric is null)
                {
                    lrc = CleanLrc(lyricResult?.Lyric?.Lyric);
                    romaji = lyricResult?.RomajiLyric?.Lyric;
                    translrc = lyricResult?.TranslationLyric?.Lyric;
                    res = new PureLyricInfo()
                    {
                        PureLyrics = lrc,
                        TrLyrics = translrc,
                        NeteaseRomaji = romaji,
                    };
                }
                else
                {
                    lrc = CleanLrc(lyricResult?.Lyric?.Lyric);
                    karaoklrc = CleanLrc(lyricResult?.YunLyric?.Lyric);
                    yrromaji = lyricResult?.YunRomajiLyric?.Lyric;
                    yrtranslrc = lyricResult?.YunTranslationLyric?.Lyric;
                    romaji = lyricResult?.RomajiLyric?.Lyric;
                    translrc = lyricResult?.TranslationLyric?.Lyric;
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
                if (lyricResult?.LyricUser?.UserId is not null)
                {
                    res.LyricMetadata.Add(new LyricInfoMetadata()
                    {
                        Key = "lyric_user",
                        Value = lyricResult.LyricUser.Nickname,
                        ActionUri = $"hyplayer://us{lyricResult.LyricUser.UserId}",
                        DisplayName = "歌词贡献者"
                    });
                }

                if (lyricResult?.TranslationUser?.UserId is not null)
                {
                    res.LyricMetadata.Add(new LyricInfoMetadata()
                    {
                        Key = "translation_user",
                        Value = lyricResult.TranslationUser.Nickname,
                        ActionUri = $"hyplayer://us{lyricResult.TranslationUser.UserId}",
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
            FadeManager.PauseFadeProcessing();
            if (string.IsNullOrEmpty(Common.Setting.AudioRenderDevice)) await Player.ChangePlayerServiceImplementation(new AudioGraphAudioSetting() { OutputVolume = PlayerOutgoingVolume });
            else await Player.ChangePlayerServiceImplementation(new AudioGraphAudioSetting() { OutputVolume = PlayerOutgoingVolume, DefaultDeviceId = Common.Setting.AudioRenderDevice, EnableFFTProcessing = Common.Setting.EnableFFT });
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists("在切换输出设备时发生错误", ex.Message);
            await Player.ChangePlayerServiceImplementation(new AudioGraphAudioSetting() { OutputVolume = PlayerOutgoingVolume, EnableFFTProcessing = Common.Setting.EnableFFT });
        }
        finally
        {
            FadeManager.ResumeFadeProcessing();
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
            Id = ncSong.sid,
            Translation = ncSong.transname,
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
                    var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.SongDetail,
                        "ncm" + sourceId.Substring(2, sourceId.Length - 2),
                        async () =>
                        {
                            var result = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                                new SongDetailRequest()
                                {
                                    Id = sourceId.Substring(2, sourceId.Length - 2)
                                });
                            if (result.IsError)
                            {
                                Common.AddToTeachingTipLists("获取歌曲信息失败", result.Error?.Message);
                                return null;
                            }
                            else
                            {
                                if (result.Value?.Songs is not { Length: > 0 })
                                {
                                    Common.AddToTeachingTipLists("获取歌曲信息失败", "歌曲信息为空");
                                    return null;
                                }

                                return result.Value.Songs[0];
                            }
                        });
                    if (rst is not null)
                        AppendNcSong(rst.MapToNcSong());
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
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, id, async () =>
            {
                var j1 = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistTopSongApi,
                    new ArtistTopSongRequest
                    {
                        ArtistId = id
                    });
                if (j1.IsError)
                {
                    Common.AddToTeachingTipLists("获取歌手热门歌曲失败", j1.Error?.Message);
                    return null;
                }

                return j1.Value?.Songs;
            }, cancellationToken: CancellationToken.None);

            AppendNcSongs(rst.Select(t => t.MapNcSong()).ToList(), false);
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
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, albumId, async () =>
            {
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumApi,
                    new AlbumRequest()
                    {
                        Id = albumId
                    });
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("获取专辑信息失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            }, cancellationToken: CancellationToken.None);


            if (rst is null)
            {
                return false;
            }

            AppendNcSongs(rst.Songs?.Select(t => t.MapToNcSong()).ToList(), false);

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
            {
                try
                {
                    var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.DjChannelProgramsApi,
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

                    hasMore = json.Value is { Data.More: true };
                    if (json.Value?.Data?.Programs is { Length: > 0 })
                        AppendNcSongs(
                            json.Value.Data.Programs.Select(t => (NCSong)t.MapToNCFmItem()).ToList(),
                            false);
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }

                page++;
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
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracks, playlistId, async () =>
            {
                var detailResponse = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                    new PlaylistTracksGetRequest() { Id = playlistId });
                if (detailResponse.IsError)
                {
                    Common.AddToTeachingTipLists("获取歌单失败", detailResponse.Error.Message);
                    return null;
                }

                return detailResponse.Value;
            }, cancellationToken: CancellationToken.None);


            var nowIndex = 0;

            var trackIds = resp?.Playlist?.TrackIds.Select(t => t.Id).ToList() ?? [];
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                try
                {
                    var songDetailResp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracksDetail, playlistId + "_" + nowIndex, async () =>
                    {
                        var songResponse = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                            new SongDetailRequest() { IdList = nowIds });
                        if (songResponse.IsError)
                        {
                            Common.AddToTeachingTipLists("获取歌曲失败", songResponse.Error?.Message);
                        }

                        return songResponse.Value;
                    }, cancellationToken: CancellationToken.None);

                    var songs = songDetailResp.Songs;
                    var privileges = songDetailResp.Privileges;

                    nowIndex++;

                    var result = new List<NCSong>();
                    if (privileges is null) return false;
                    for (var i = 0; i < privileges.Length; i++)
                    {
                        if (privileges[i].St == 0)
                        {
                            result.Add(songs![i].MapToNcSong());
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
        List.Add(await LoadStorageFile(sf, CancellationToken.None));
        return true;
    }

    public static async Task<HyPlayItem> LoadStorageFile(StorageFile sf, CancellationToken ctk = default, bool nocheck163 = false)
    {
        ctk.ThrowIfCancellationRequested();

        using var abstraction = new UwpStorageFileAbstraction(sf);
        using var tagFile = File.Create(abstraction);
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
                    Size = 0,
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

        if (string.IsNullOrEmpty(mi.musicName)) return await LoadStorageFile(sf, CancellationToken.None, true);

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
        _ = Common.Invoke(() => OnPlayListAddDone?.Invoke(true));
        return Task.CompletedTask;
    }

    public static void CheckABTimeRemaining(TimeSpan currentTime)
    {
        if (currentTime >= Common.Setting.ABEndPoint && Common.Setting.ABEndPoint != TimeSpan.Zero &&
            Common.Setting.ABEndPoint > Common.Setting.ABStartPoint)
            Seek(Common.Setting.ABStartPoint);
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
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
        return parsedlyrics.Lines.OrderBy(t => t.StartTime).Select(lyricsLine => new SongLyric
        { LyricLine = lyricsLine, Translation = null })
            .ToList();
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
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
        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrTrLyrics.AsSpan());
        if (Common.Setting.MigrateLyrics)
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.TrLyrics.AsSpan());
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
        var parsedlyrics = LrcParser.ParseLrc(lyricAllText.AsSpan());
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
        var targetLyrics = LrcParser.ParseLrc(lyricInfo.YrNeteaseRomaji.AsSpan());
        if (Common.Setting.MigrateLyrics)
        {
            var sourceLyrics = LrcParser.ParseLrc(lyricInfo.NeteaseRomaji.AsSpan());
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
                var list = await Common.KawazuConv.GetDivisions(lyricItem.LyricLine.CurrentLyric, To.Romaji,
                    Mode.Separated, RomajiSystem.Hepburn, "", "");
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
                    wordInfo[wordInfo.Count - 1].Transliteration +=
                        Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                }

                break;
            }

            if (curElement.Contains(wordInfo[i + delta].CurrentWords.Trim()))
            {
                wordInfo[i + delta].Transliteration =
                    Utilities.ToRawRomaji(curHiraNotation, RomajiSystem.Hepburn, true);
                if (!string.IsNullOrWhiteSpace(wordInfo[i + delta].CurrentWords))
                {
                    var trimmedWord = wordInfo[i + delta].CurrentWords.Trim();
                    var idx = curElement.IndexOf(trimmedWord, StringComparison.Ordinal);
                    if (idx >= 0)
                        curElement = curElement.Remove(idx, trimmedWord.Length);
                }

                if (curElement.Trim().Length > 0)
                {
                    wordInfo[i + delta].Transliteration =
                        Utilities.ToRawRomaji(curHiraNotation.Substring(0, 1), RomajiSystem.Hepburn, true);
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
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
                    else ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                else
                    await ConvertKawazuRomaji(lyrics);
                break;
            case RomajiSource.NeteaseOnly:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    if (pureLyricInfo is KaraokLyricInfo karaokLyricInfo)
                        ConvertYrcNeteaseRomaji(karaokLyricInfo, lyrics);
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
            var parsedLyrics = KaraokeParser.ParseKaraoke(((KaraokLyricInfo)pureLyricInfo).KaraokLyric.AsSpan());
            if (Common.Setting.MigrateLyrics)
            {
                var pureLyrics = LrcParser.ParseLrc(pureLyricInfo.PureLyrics.AsSpan());
                var migrated = MigrationTool.Migrate(parsedLyrics, pureLyrics);
                return migrated.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t }).ToList();
            }

            return parsedLyrics.Lines.OrderBy(t => t.StartTime).Select(t => new SongLyric() { LyricLine = t }).ToList();
        }

        throw new ArgumentException("HyLyricInfo is not KaraokeLyricInfo");
    }
}

public class AudioDevices
{
    public string DeviceID;
    public string DeviceName;
    public bool IsDefaultDevice;
}