﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using HyPlayer.Classes;
using Kawazu;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using Opportunity.LrcParser;
using File = TagLib.File;

#endregion

namespace HyPlayer.HyPlayControl;

public static class HyPlayList
{
    public delegate void LoginDoneEvent();

    public delegate void LyricChangeEvent();

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

    private static int _gcCountDown = 5;

    public static int NowPlaying;
    private static readonly Timer SecTimer = new(1000); // 公用秒表
    public static readonly List<HyPlayItem> List = new();
    public static readonly List<int> ShuffleList = new();
    public static int ShufflingIndex = -1;
    public static List<SongLyric> Lyrics = new();
    public static TimeSpan LyricOffset = TimeSpan.Zero;
    
    /********        API        ********/
    public static MediaPlayer Player;
    public static SystemMediaTransportControls MediaSystemControls;
    private static SystemMediaTransportControlsDisplayUpdater _controlsDisplayUpdater;
    private static readonly BackgroundDownloader Downloader = new();

    public static int LyricPos;
    private static string _crashedTime;

    public static string PlaySourceId;
    private static double _playerOutgoingVolume;

    public static double PlayerOutgoingVolume
    {
        get => _playerOutgoingVolume;
        set
        {
            _playerOutgoingVolume = value;
            Player.Volume = _playerOutgoingVolume;
            Common.Setting.Volume = (int)(value * 100);
            OnVolumeChange?.Invoke(_playerOutgoingVolume);
        }
    }

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
            if (Player.Source != null)
            {
                return (Player.Source as MediaSource).CustomProperties["nowPlayingItem"] as HyPlayItem;
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

    public static event MediaEndEvent OnMediaEnd;

    public static event LyricChangeEvent OnSongMoveNext;

    public static event LyricChangeEvent OnSongBufferStart;

    public static event LyricChangeEvent OnSongBufferEnd;

    public static event LoginDoneEvent OnLoginDone;

    public static event TimerTicked OnTimerTicked;

    public static event SongRemoveAllEvent OnSongRemoveAll;

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

        Player.MediaFailed += (sender, reason) =>
        {
            PlayerOnMediaFailed(sender, "播放核心：" + reason.ErrorMessage + " " + reason.Error);
        };
        Player.BufferingStarted += Player_BufferingStarted;
        Player.BufferingEnded += Player_BufferingEnded;
        Player.SourceChanged += Player_SourceChanged;
        SecTimer.Elapsed += (sender, args) => _ = Common.Invoke(() => OnTimerTicked?.Invoke());
        SecTimer.Start();
        OnTimerTicked += () =>
        {
            if (--_gcCountDown >= 0) return;
            _gcCountDown = 5;
            GC.Collect();
        };
        HistoryManagement.InitializeHistoryTrack();
        Common.IsInFm = false;
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
        Player.PlaybackSession.Position = args.RequestedPlaybackPosition;
    }


    public static void LoginDoneCall()
    {
        _ = Common.Invoke(() => { OnLoginDone?.Invoke(); });
    }

    private static void Player_BufferingEnded(MediaPlayer sender, object args)
    {
        _ = Common.Invoke(() => OnSongBufferEnd?.Invoke());
    }

    private static void Player_BufferingStarted(MediaPlayer sender, object args)
    {
        _ = Common.Invoke(() => OnSongBufferStart?.Invoke());
    }

    private static void PlayerOnMediaFailed(MediaPlayer sender, string reason)
    {
        //歌曲崩溃了的话就是这个
        //SongMoveNext();
        Common.ErrorMessageList.Add("歌曲" + NowPlayingItem.PlayItem.Name + " 播放失败: " + reason);

        Common.AddToTeachingTipLists("播放失败 切到下一曲",
            "歌曲" + NowPlayingItem.PlayItem.Name + "\r\n" + reason);
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
                var stream = await file.OpenStreamForReadAsync();
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

                stream.Dispose();
            }
            else
            {
                await AppendStorageFile(file);
            }

            if (!isFirstLoad) continue;
            SongAppendDone();
            isFirstLoad = false;
            SongMoveTo(List.Count - 1);
        }

        SongAppendDone();
        //HyPlayList.SongMoveTo(0);
    }


    private static async Task LoadLocalFile()
    {
        // 此处可以改进
        if ( /*_lastStorageUrl != NowPlayingItem.PlayItem.url*/ true)
        {
            if (NowPlayingItem.PlayItem.DontSetLocalStorageFile != null)
            {
                if (NowPlayingItem.PlayItem.DontSetLocalStorageFile.FileType != ".ncm" &&
                    NowPlayingItem.ItemType != HyPlayItemType.LocalProgressive)
                {
                    NowPlayingStorageFile = NowPlayingItem.PlayItem.DontSetLocalStorageFile;
                }
                else
                {
                    if (NowPlayingItem.PlayItem.DontSetLocalStorageFile.FileType == ".ncm")
                    {
                        // 脑残Music解析
                        var stream = (await NowPlayingItem.PlayItem.DontSetLocalStorageFile.OpenReadAsync())
                            .AsStreamForRead();
                        if (NCMFile.IsCorrectNCMFile(stream))
                        {
                            var info = NCMFile.GetNCMMusicInfo(stream);
                            var coverStream = NCMFile.GetCoverStream(stream);
                            var encStream = NCMFile.GetEncryptedStream(stream);
                            encStream.Seek(0, SeekOrigin.Begin);
                            NowPlayingStorageFile = await StorageFile.CreateStreamedFileAsync(
                                Path.ChangeExtension(NowPlayingItem.PlayItem.DontSetLocalStorageFile.Name,
                                    info.format), t => { encStream.CopyTo(t.AsStreamForWrite()); },
                                RandomAccessStreamReference.CreateFromStream(
                                    coverStream.AsRandomAccessStream()));
                        }
                    }
                    else
                    {
                        NowPlayingStorageFile = NowPlayingItem.PlayItem.DontSetLocalStorageFile;
                        var item = await LoadStorageFile(NowPlayingItem.PlayItem.DontSetLocalStorageFile);
                        NowPlayingItem.ItemType = HyPlayItemType.Local;
                        NowPlayingItem.PlayItem = item.PlayItem;
                        NowPlayingItem.PlayItem.DontSetLocalStorageFile = NowPlayingStorageFile;
                    }
                }
            }
            else
            {
                NowPlayingStorageFile = await StorageFile.GetFileFromPathAsync(NowPlayingItem.PlayItem.Url);
            }
        }

        //Player_SourceChanged(null, null);
    }

    /********        方法         ********/
    public static void SongAppendDone()
    {
        Common.IsInFm = false;
        PlaySourceId = null;
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
            CreateShufflePlayLists();
        else
            _ = Common.Invoke(() => OnPlayListAddDone?.Invoke());
    }

    public static void SongMoveNext()
    {
        OnSongMoveNext?.Invoke();
        if (List.Count == 0) return;
        MoveSongPointer(true);
        _ = LoadPlayerSong(List[NowPlaying]);
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

        _ = LoadPlayerSong(List[NowPlaying]);
    }

    public static void SongMoveTo(int index)
    {
        if (List.Count <= index) return;
        NowPlaying = index;
        if (NowPlayType == PlayMode.Shuffled && Common.Setting.shuffleNoRepeating)
            ShufflingIndex = ShuffleList.FindIndex(t => t == index);

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
    }

    public static void ManualRemoveAllSong()
    {
        RemoveAllSong();
        _ = Common.Invoke(() => OnPlayItemChange?.Invoke(null));
    }

    public static void RemoveAllSong(bool resetPlaying = true)
    {
        List.Clear();
        if (resetPlaying)
            Player.Source = null;
        NowPlaying = -1;
        OnSongRemoveAll?.Invoke();
        SongAppendDone();
    }

    /********        相关事件处理        ********/

    private static void SystemControls_ButtonPressed(SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                Player.Play();
                break;
            case SystemMediaTransportControlsButton.Pause:
                Player.Pause();
                break;
            case SystemMediaTransportControlsButton.Previous:
                SongMovePrevious();
                break;
            case SystemMediaTransportControlsButton.Next:
                SongMoveNext();
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
        _ = LoadPlayerSong(List[NowPlaying]);
    }

    private static async Task<string> GetNowPlayingUrl(HyPlayItem targetItem)
    {
        var playUrl = targetItem.PlayItem.Url;
        // 对了,先看看是否要刷新播放链接
        if (string.IsNullOrEmpty(targetItem.PlayItem.Url) ||
            Common.Setting.songUrlLazyGet)
            try
            {
                var json = await Common.ncapi.RequestAsync(
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
                    var tag = json["data"]?[0]?["level"]?.ToString() switch
                    {
                        "standard" => "128K",
                        "exhigh" => "320K",
                        "lossless" => "无损",
                        "hires" => "Hi-Res",
                        _ => "在线"
                    };
                    targetItem.PlayItem.Tag = tag;
                    _ = Common.Invoke(() => { Common.BarPlayBar.TbSongTag.Text = tag; });
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
        if (targetItem.PlayItem?.Name == null)
        {
            MoveSongPointer();
            return;
        }

        MediaSource ms = null;
        try
        {
            switch (targetItem.ItemType)
            {
                case HyPlayItemType.Netease:
                case HyPlayItemType.Radio: //FM伪加载为普通歌曲
                    //先看看是不是本地文件
                    //本地文件的话尝试加载
                    //cnm的NCM,我试试其他方式
                    if (targetItem.PlayItem.IsLocalFile)
                    {
                        await LoadLocalFile();
                        ms = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
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
                                    ms = MediaSource.CreateFromStorageFile(sf);
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
                                        var destinationFile =
                                            await (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
                                                "songCache",
                                                CreationCollisionOption.OpenIfExists)).CreateFileAsync(
                                                targetItem.PlayItem.Id +
                                                ".cache",
                                                CreationCollisionOption.ReplaceExisting);
                                        var downloadOperation =
                                            Downloader.CreateDownload(new Uri(playUrl), destinationFile);
                                        downloadOperation.IsRandomAccessRequired = true;
                                        ms = MediaSource.CreateFromDownloadOperation(downloadOperation);
                                    }
                                }
                                catch
                                {
                                    var playUrl = await GetNowPlayingUrl(targetItem);
                                    if (playUrl != null)
                                        ms = MediaSource.CreateFromUri(new Uri(playUrl));
                                }
                            }
                        }
                        else
                        {
                            var playUrl = await GetNowPlayingUrl(targetItem);
                            ms = MediaSource.CreateFromUri(new Uri(playUrl));
                        }
                    }

                    break;
                case HyPlayItemType.Local:
                case HyPlayItemType.LocalProgressive:
                    try
                    {
                        await LoadLocalFile();
                        ms = MediaSource.CreateFromStorageFile(NowPlayingStorageFile);
                    }
                    catch
                    {
                        ms = MediaSource.CreateFromUri(new Uri(targetItem.PlayItem.Url));
                    }

                    break;
                default:
                    ms = null;
                    break;
            }
            ms.CustomProperties.Add("nowPlayingItem", targetItem);
            MediaSystemControls.IsEnabled = true;
            await ms.OpenAsync();
            Player.Source = ms;
        }
        catch (Exception e)
        {
            Player.Source = null;
            PlayerOnMediaFailed(Player, e.Message);
        }
    }

    public static async void Player_SourceChanged(MediaPlayer sender, object args)
    {
        if (List.Count <= NowPlaying) return;
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
            if (NowPlayingItem.ItemType == HyPlayItemType.Netease)
                _controlsDisplayUpdater.MusicProperties.Genres.Add("NCM-" + NowPlayingItem.PlayItem.Id);

            //记录下当前播放位置
            ApplicationData.Current.LocalSettings.Values["nowSongPointer"] = NowPlaying.ToString();
        }

        //因为加载图片可能会高耗时,所以在此处加载
        _ = Common.Invoke(() => OnPlayItemChange?.Invoke(NowPlayingItem));
        //加载歌词
        if (NowPlayingItem.PlayItem != null)
        {
            _ = LoadLyrics(NowPlayingItem);
            try
            {
                if (NowPlayingItem.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
                {
                    if (!Common.Setting.useTaglibPicture || NowPlayingItem.PlayItem.LocalFileTag is null ||
                        NowPlayingItem.PlayItem.LocalFileTag.Pictures.Length == 0)
                    {
                        if (NowPlayingStorageFile != null)
                        {
                            var thumbnail =
                                await NowPlayingStorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, 9999);
                            if (thumbnail is { CanRead: true })
                                _controlsDisplayUpdater.Thumbnail =
                                    RandomAccessStreamReference.CreateFromStream(thumbnail);
                            else
                                RandomAccessStreamReference.CreateFromUri(new Uri("/Assets/icon.png",
                                    UriKind.Relative));
                        }
                    }
                    else
                    {
                        _controlsDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(
                            new MemoryStream(NowPlayingItem.PlayItem.LocalFileTag.Pictures[0].Data.Data)
                                .AsRandomAccessStream());
                    }
                }
                else
                {
                    _controlsDisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(
                        NowPlayingItem.PlayItem.Album.cover +
                        "?param=" +
                        StaticSource.PICSIZE_AUDIO_PLAYER_COVER));
                }
            }
            catch (Exception)
            {
                //ignore
            }

            _controlsDisplayUpdater.Update();
        }
    }

    private static void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
    {
        OnPlayPositionChange?.Invoke(Player.PlaybackSession.Position);
        LoadLyricChange();
    }

    private static void LoadLyricChange()
    {
        if (Lyrics.Count == 0) return;
        if (LyricPos >= Lyrics.Count || LyricPos < 0) LyricPos = 0;
        var changed = false;
        var realPos = Player.PlaybackSession.Position - LyricOffset;
        if (Lyrics[LyricPos].LyricTime > realPos) //当感知到进度回溯时执行
        {
            LyricPos = Lyrics.FindLastIndex(t => t.LyricTime <= realPos) - 1;
            if (LyricPos == -2) LyricPos = -1;
            changed = true;
        }

        try
        {
            if (LyricPos == 0 && Lyrics.Count != 1) changed = false;
            while (Lyrics.Count > LyricPos + 1 &&
                   Lyrics[LyricPos + 1].LyricTime <= realPos) //正常的滚歌词
            {
                LyricPos++;
                changed = true;
            }
        }
        catch
        {
            // ignored
        }


        if (changed) OnLyricChange?.Invoke();
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
        Lyrics = Utils.ConvertPureLyric(pureLyricInfo.PureLyrics, unionTranslation);
        if (Lyrics.Count == 0)
        {
            if (Common.Setting.showComposerInLyric)
                Lyrics.Add(new SongLyric
                {
                    LyricTime = TimeSpan.Zero,
                    PureLyric = pureLyricInfo.PureLyrics
                });
        }
        else
        {
            Utils.ConvertTranslation(pureLyricInfo.TrLyrics, Lyrics);
            await Utils.ConvertRomaji(pureLyricInfo, Lyrics);

            if (Lyrics.Count != 0 && Lyrics[0].LyricTime != TimeSpan.Zero)
                Lyrics.Insert(0,
                    new SongLyric { LyricTime = TimeSpan.Zero, PureLyric = "" });
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

                json = await Common.ncapi.RequestAsync(
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
                        json = await Common.ncapi.RequestAsync(
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
        if (position < 0)
            position = List.Count;
        if (hpi != null)
            List.Insert(position, hpi);
        return hpi;
    }

    public static List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        if (position < 0)
            position = List.Count;
        var insertList = ncSongs.Select(LoadNcSong).ToList();
        List.InsertRange(position, insertList);
        return insertList;
    }

    private static HyPlayItem LoadNcSong(NCSong ncSong)
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

    public static void AppendNcSongs(IList<NCSong> ncSongs,
        bool needRemoveList = true)
    {
        if (ncSongs == null) return;
        if (needRemoveList)
            RemoveAllSong();
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
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
                        new Dictionary<string, object>
                            { { "ids", sourceId.Substring(2, sourceId.Length - 2) } });
                    _ = AppendNcSong(NCSong.CreateFromJson(json["songs"]?[0]));
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
            var j1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistTopSong,
                new Dictionary<string, object> { { "id", id } });


            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
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
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Album,
                new Dictionary<string, object> { { "id", albumId } });

            var list = new List<NCSong>();
            foreach (var song in (json["songs"] ?? new JArray()).ToArray())
            {
                var ncSong = NCSong.CreateFromJson(song);
                list.Add(ncSong);
            }

            list.RemoveAll(t => t == null);
            AppendNcSongs(list, false);


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
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjProgram,
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
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.PlaylistDetail,
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
                    json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SongDetail,
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
        var mdp = await sf.Properties.GetMusicPropertiesAsync();
        var abstraction = new UwpStorageFileAbstraction(sf);
        var tagFile = File.Create(abstraction);
        if (nocheck163 ||
            !The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var mi))
        {
            //TagLib.File afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            var contributingArtists =
                string.IsNullOrEmpty(mdp.Artist) ? "未知歌手" : mdp.Artist;


            var hyPlayItem = new HyPlayItem
            {
                PlayItem = new PlayItem
                {
                    IsLocalFile = true,
                    LocalFileTag = tagFile.Tag,
                    Bitrate = (int)mdp.Bitrate,
                    Tag = sf.Provider.DisplayName,
                    Id = null,
                    Name = string.IsNullOrWhiteSpace(mdp.Title) ? sf.Name : mdp.Title,
                    Type = HyPlayItemType.Local,
                    Artist = new List<NCArtist>
                        { new NCArtist { name = contributingArtists, Type = HyPlayItemType.Local } },
                    Album = new NCAlbum
                    {
                        name = mdp.Album
                    },
                    TrackId = (int)mdp.TrackNumber,
                    CDName = "01",
                    Url = sf.Path,
                    SubExt = sf.FileType,
                    Size = "0",
                    LengthInMilliseconds = mdp.Duration.TotalMilliseconds
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
            LengthInMilliseconds = mdp.Duration.TotalMilliseconds,
            Id = mi.musicId.ToString(),
            Artist = null,
            Name = mi.musicName,
            TrackId = (int)mdp.TrackNumber,
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

    public static Task CreateShufflePlayLists()
    {
        if (List.Count != 0)
        {
            ShuffleList.Clear();
            HashSet<int> shuffledNumbers = new();
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
                ShufflingIndex = ShuffleList.FindIndex(t => t == NowPlaying);
        }

        // Call 一下来触发前端显示的播放列表更新
        _ = Common.Invoke(() => OnPlayListAddDone?.Invoke());
        return Task.CompletedTask;
    }

    public static void CheckABTimeRemaining(TimeSpan currentTime)
    {
        if (currentTime >= Common.Setting.ABEndPoint && Common.Setting.ABEndPoint != TimeSpan.Zero &&
            Common.Setting.ABEndPoint > Common.Setting.ABStartPoint)
            Player.PlaybackSession.Position = Common.Setting.ABStartPoint;
    }

    public static async void UpdateLastFMNowPlayingAsync(HyPlayItem NowPlayingItem)
    {
        if (NowPlayingItem.PlayItem != null && NowPlayingItem.ItemType == HyPlayItemType.Netease)
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
        var parsedlyrics = Lyrics.Parse(lyricAllText);
        return parsedlyrics.Lyrics.Lines.Select(lyricsLine => new SongLyric
                { LyricTime = lyricsLine.Timestamp.TimeOfDay, PureLyric = lyricsLine.Content, Translation = null })
            .OrderBy(t => t.LyricTime)
            .ToList();
    }

    public static void ConvertTranslation(string lyricAllText, List<SongLyric> lyrics)
    {
        var parsedlyrics = Lyrics.Parse(lyricAllText);
        foreach (var lyricsLine in parsedlyrics.Lyrics.Lines)
        foreach (var songLyric in lyrics.Where(songLyric =>
                     songLyric.LyricTime.TotalMilliseconds == lyricsLine.Timestamp.TimeOfDay.TotalMilliseconds))
        {
            songLyric.Translation = lyricsLine.Content;
            break;
        }
    }

    public static void ConvertNeteaseRomaji(string lyricAllText, List<SongLyric> lyrics)
    {
        if (string.IsNullOrEmpty(lyricAllText)) return;
        var parsedlyrics = Lyrics.Parse(lyricAllText);
        foreach (var lyricsLine in parsedlyrics.Lyrics.Lines)
        foreach (var songLyric in lyrics.Where(songLyric =>
                     songLyric.LyricTime.TotalMilliseconds == lyricsLine.Timestamp.TimeOfDay.TotalMilliseconds))
        {
            songLyric.Romaji = lyricsLine.Content;
            break;
        }
    }

    public static async Task ConvertKawazuRomaji(List<SongLyric> lyrics)
    {
        if (Common.KawazuConv is null) return;
        foreach (var lyricItem in lyrics)
        {
            if (!string.IsNullOrWhiteSpace(lyricItem.PureLyric))
            {
                if (Utilities.HasKana(lyricItem.PureLyric))
                    lyricItem.Romaji = await Common.KawazuConv.Convert(lyricItem.PureLyric, To.Romaji, Mode.Separated);
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
                    ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                else
                    await ConvertKawazuRomaji(lyrics);
                break;
            case RomajiSource.NeteaseOnly:
                if (!string.IsNullOrEmpty(pureLyricInfo.NeteaseRomaji))
                    ConvertNeteaseRomaji(pureLyricInfo.NeteaseRomaji, lyrics);
                break;
            case RomajiSource.KawazuOnly:
                await ConvertKawazuRomaji(lyrics);
                break;
        }
    }
}

public class AudioDevices
{
    public string DeviceID;
    public string DeviceName;
    public bool IsDefaultDevice;
}