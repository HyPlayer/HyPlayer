using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace HyPlayer.Services.Playback;

/// <summary>
/// 播放列表服务 — 编排播放策略 (<see cref="IPlayStrategy"/>) 与过渡策略 (<see cref="ITrackTransition"/>)，
/// 管理播放列表内容和播放顺序。
/// </summary>
public sealed partial class PlaylistService : IPlaylistService, IDisposable
{
    private readonly Dictionary<string, IPlayStrategy> _strategies;
    private readonly Dictionary<string, ITrackTransition> _transitions;
    private readonly PlaybackStateService _state;
    private readonly IPlaybackControlService _control;
    private readonly IPlayer _player;
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INotificationService _notification;
    private readonly Setting _setting;

    private readonly List<HyPlayItem> _items = new();
    private readonly object _lock = new();

    private IPlayStrategy _activeStrategy;
    private ITrackTransition _activeTransition;
    private int _nowPlayingIndex = -1;
    private CancellationTokenSource? _trackEndCts;
    private readonly SemaphoreSlim _trackEndLock = new(1, 1);

    /// <summary>
    /// 初始化 <see cref="PlaylistService"/>。
    /// </summary>
    /// <param name="strategies">所有已注册的播放策略</param>
    /// <param name="transitions">所有已注册的过渡策略</param>
    /// <param name="state">播放状态中心</param>
    /// <param name="control">播放控制服务</param>
    /// <param name="player">底层播放器</param>
    /// <param name="api">网易云 API 处理器</param>
    public PlaylistService(
        IEnumerable<IPlayStrategy> strategies,
        IEnumerable<ITrackTransition> transitions,
        PlaybackStateService state,
        IPlaybackControlService control,
        IPlayer player,
        NeteaseCloudMusicApiHandler api,
        INotificationService notification,
        Setting setting)
    {
        _strategies = strategies.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _transitions = transitions.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _state = state;
        _control = control;
        _player = player;
        _api = api;
        _notification = notification;
        _setting = setting;

        _activeStrategy = _strategies.GetValueOrDefault("seq")
                          ?? _strategies.Values.First();
        _activeTransition = _transitions.GetValueOrDefault("dir")
                            ?? _transitions.Values.First();

        _state.ActiveStrategyId = _activeStrategy.Id;
        _state.ActiveTransitionId = _activeTransition.Id;
    }

    /// <inheritdoc />
    public IReadOnlyList<HyPlayItem> Items => _items.AsReadOnly();

    /// <inheritdoc />
    public int NowPlayingIndex => _nowPlayingIndex;

    /// <inheritdoc />
    public HyPlayItem? NowPlayingItem
    {
        get
        {
            lock (_lock)
            {
                return _nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count
                    ? _items[_nowPlayingIndex]
                    : null;
            }
        }
    }

    /// <inheritdoc />
    public string ActiveStrategyId => _activeStrategy.Id;

    /// <inheritdoc />
    public string ActiveTransitionId => _activeTransition.Id;

    /// <inheritdoc />
    public bool IsInFm => _state.IsInFm;

    /// <inheritdoc />
    public string PlaySourceId { get; set; } = string.Empty;

    // ────────────── 列表操作 ──────────────

    /// <inheritdoc />
    public void AppendItem(HyPlayItem item, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0 || position >= _items.Count)
                _items.Add(item);
            else
                _items.Insert(position, item);
        }
    }

    /// <inheritdoc />
    public void AppendItems(IEnumerable<HyPlayItem> items, bool clearFirst = false)
    {
        lock (_lock)
        {
            if (clearFirst)
                _items.Clear();

            _items.AddRange(items);
        }
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count)
                return;

            if (_items.Count == 1)
            {
                Clear();
                return;
            }

            _items.RemoveAt(index);

            // 调整当前播放索引
            if (index < _nowPlayingIndex)
            {
                _nowPlayingIndex--;
                SyncIndex();
            }
            else if (index == _nowPlayingIndex)
            {
                // 当前曲目被移除，索引不变但指向了新曲目
                if (_nowPlayingIndex >= _items.Count)
                    _nowPlayingIndex = _items.Count - 1;
                SyncIndex();
            }

            SendPlaylistChanged();
        }
    }

    /// <inheritdoc />
    public void Clear(bool stopPlayback = true)
    {
        lock (_lock)
        {
            if (_items.Count == 0)
                return;

            if (stopPlayback && _player.GlobalPlaybackStatus == PlaybackStatus.Playing)
                _player.PauseAll();

            _items.Clear();
            _nowPlayingIndex = -1;
            SyncIndex();
            _state.NowPlayingItem = null;

            SendPlaylistChanged();
        }
    }

    /// <inheritdoc />
    public void NotifyAppendDone(bool isShuffleTrigger = false)
    {
        _activeStrategy.OnPlaylistChanged(BuildStrategyContext());
        SendPlaylistChanged(isShuffleTrigger);
    }

    // ────────────── 导航 ──────────────

    /// <inheritdoc />
    public async Task MoveNextAsync(bool userInitiated = false)
    {
        if (_items.Count == 0)
            return;

        if (userInitiated)
            await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null)
            return;

        HyPlayItem item;
        lock (_lock)
        {
            _nowPlayingIndex = nextIndex.Value;
            SyncIndex();
            item = _items[_nowPlayingIndex];
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MovePreviousAsync()
    {
        if (_items.Count == 0)
            return;

        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var prevIndex = _activeStrategy.GetPrevious(BuildStrategyContext());
        if (prevIndex is null)
            return;

        HyPlayItem item;
        lock (_lock)
        {
            _nowPlayingIndex = prevIndex.Value;
            SyncIndex();
            item = _items[_nowPlayingIndex];
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MoveToAsync(HyPlayItem item)
    {
        int index;
        lock (_lock)
        {
            index = _items.IndexOf(item);
        }
        if (index < 0)
            return;

        // 中断正在进行的过渡
        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        lock (_lock)
        {
            _nowPlayingIndex = index;
            SyncIndex();
        }

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    // ────────────── 曲目结束处理 ──────────────

    /// <inheritdoc />
    public async Task OnTrackEndedAsync()
    {
        if (!await _trackEndLock.WaitAsync(0)) return;

        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = new CancellationTokenSource();
        var ct = _trackEndCts.Token;

        try
        {
            var action = _activeStrategy.OnTrackEnded(BuildStrategyContext());

            switch (action)
            {
                case PlayStrategyAction.MoveNext:
                    await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    break;

                case PlayStrategyAction.Replay:
                    await _control.SeekAsync(TimeSpan.Zero);
                    break;

                case PlayStrategyAction.LoadMore:
                    if (_activeStrategy is IAsyncPlayStrategy asyncStrategy)
                    {
                        var moreItems = await asyncStrategy.LoadMoreAsync(
                            BuildStrategyContext(), ct);
                        lock (_lock)
                        {
                            _items.AddRange(moreItems);
                        }
                        NotifyAppendDone();
                        await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    }
                    break;

                case PlayStrategyAction.Stop:
                    // 服务器驱动模式，不做任何操作
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 新的播放结束处理已接管。
        }
        finally
        {
            _trackEndLock.Release();
        }
    }

    /// <inheritdoc />
    public void OnPositionTick(TimeSpan position, TimeSpan duration)
    {
        _activeTransition.OnPositionTick(BuildTransitionContext());
    }

    // ────────────── 策略切换 ──────────────

    /// <inheritdoc />
    public void SetStrategy(string strategyId)
    {
        if (!_strategies.TryGetValue(strategyId, out var strategy))
            return;

        _activeStrategy = strategy;
        _state.ActiveStrategyId = strategyId;
        _activeStrategy.OnPlaylistChanged(BuildStrategyContext());
    }

    /// <inheritdoc />
    public void SetTransition(string transitionId)
    {
        if (!_transitions.TryGetValue(transitionId, out var transition))
            return;

        _activeTransition.Reset();
        _activeTransition = transition;
        _state.ActiveTransitionId = transitionId;
    }

    // ────────────── 本地文件追加（占位） ──────────────

    /// <inheritdoc />
    public Task AppendStorageFilesAsync(IEnumerable<StorageFile> files)
    {
        // 本地文件加载逻辑由 MediaProvider 层处理，此处仅作接口占位
        return Task.CompletedTask;
    }

    // ────────────── NCSong 相关 ──────────────

    /// <inheritdoc />
    public HyPlayItem NCSongToPlayItem(NCSong ncSong)
    {
        return new HyPlayItem
        {
            ItemType = ncSong.Type,
            InfoTag = ncSong.Alias,
            Album = ncSong.Album,
            Artist = ncSong.Artist,
            Id = ncSong.SongId,
            Translation = ncSong.TranslatedName,
            Name = ncSong.SongName,
            TrackId = ncSong.TrackId,
            CDName = ncSong.CDName,
            LengthInMilliseconds = ncSong.LengthInMilliseconds
        };
    }

    /// <inheritdoc />
    public HyPlayItem AppendNcSong(NCSong ncSong, int position = -1)
    {
        var hpi = NCSongToPlayItem(ncSong);
        lock (_lock)
        {
            if (_items.Contains(hpi))
                return hpi;

            if (position < 0 || position >= _items.Count)
                _items.Add(hpi);
            else
                _items.Insert(position, hpi);
        }

        NotifyAppendDone();
        return hpi;
    }

    /// <inheritdoc />
    public void AppendNcSongs(IList<NCSong> ncSongs, bool clearFirst = true, string currentSongId = "-1")
    {
        if (ncSongs == null) return;
        try
        {
            if (clearFirst)
            {
                lock (_lock) { _items.Clear(); }
            }

            foreach (var ncSong in ncSongs)
            {
                var hpi = NCSongToPlayItem(ncSong);
                lock (_lock) { _items.Add(hpi); }
            }

            NotifyAppendDone();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSong时发生错误", ex.Message);
        }
    }

    /// <inheritdoc />
    public List<HyPlayItem> AppendNcSongRange(List<NCSong> ncSongs, int position = -1)
    {
        lock (_lock)
        {
            if (position < 0)
                position = _items.Count;

            var insertList = ncSongs.Select(NCSongToPlayItem)
                .Where(t => !_items.Contains(t))
                .ToList();

            if (insertList.Count <= 0)
                return insertList;

            _items.InsertRange(position, insertList);
            NotifyAppendDone();
            return insertList;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AppendNcSourceAsync(string sourceId)
    {
        try
        {
            var prefix = sourceId[..2];
            switch (prefix)
            {
                case "pl":
                    await AppendPlayListAsync(sourceId[2..]);
                    return true;
                case "ns":
                    var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.SongDetail,
                        string.Concat("ncm", sourceId.AsSpan(2, sourceId.Length - 2)),
                        async () =>
                        {
                            var result = await _api.RequestAsync(NeteaseApis.SongDetailApi,
                                new SongDetailRequest { Id = sourceId[2..] });
                            if (result.IsError)
                            {
                                _notification.ShowMessage("获取歌曲信息失败", result.Error?.Message);
                                return null;
                            }

                            if (result.Value?.Songs is not { Length: > 0 })
                            {
                                _notification.ShowMessage("获取歌曲信息失败", "歌曲信息为空");
                                return null;
                            }

                            return result.Value.Songs[0];
                        });
                    if (rst is not null)
                        AppendNcSong(rst.MapToNcSong());
                    return true;
                case "al":
                    await AppendAlbumAsync(sourceId[2..]);
                    return true;
                case "sh":
                case "sa":
                    await AppendSingerHotAsync(sourceId[2..]);
                    return true;
                case "rd":
                    await AppendRadioListAsync(sourceId[2..]);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AppendPlayListAsync(string playlistId)
    {
        try
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracks, playlistId, async () =>
            {
                var detailResponse = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                    new PlaylistTracksGetRequest { Id = playlistId });
                if (detailResponse.IsError)
                {
                    _notification.ShowMessage("获取歌单失败", detailResponse.Error.Message);
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
                var songDetailResp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracksDetail,
                    playlistId + "_" + nowIndex, async () =>
                    {
                        var songResponse = await _api.RequestAsync(NeteaseApis.SongDetailApi,
                            new SongDetailRequest { IdList = nowIds });
                        if (songResponse.IsError)
                            _notification.ShowMessage("获取歌曲失败", songResponse.Error?.Message);
                        return songResponse.Value;
                    }, cancellationToken: CancellationToken.None);

                var songs = songDetailResp.Songs;
                nowIndex++;
                var result = songs.Select(t => t.MapToNcSong()).ToList();
                AppendNcSongs(result, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendPlayList时发生错误", ex.Message);
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<bool> AppendRadioListAsync(string radioId, bool asc = false)
    {
        try
        {
            bool? hasMore = true;
            var page = 0;
            while (hasMore is true)
            {
                var json = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,
                    new DjChannelProgramsRequest
                    {
                        RadioId = radioId,
                        Offset = page * 100,
                        Limit = 100,
                        Asc = asc
                    });
                if (json.IsError)
                {
                    _notification.ShowMessage("获取电台节目失败", json.Error.Message);
                    return false;
                }

                hasMore = json.Value is { Data.More: true };
                if (json.Value?.Data?.Programs is { Length: > 0 })
                    AppendNcSongs(
                        [.. json.Value.Data.Programs.Select(t => (NCSong)t.MapToNCFmItem())],
                        false);

                page++;
            }

            return true;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendRadioList时发生错误", ex.Message);
        }

        return false;
    }

    private async Task<bool> AppendSingerHotAsync(string id)
    {
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, id, async () =>
            {
                var j1 = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,
                    new ArtistTopSongRequest { ArtistId = id });
                if (j1.IsError)
                {
                    _notification.ShowMessage("获取歌手热门歌曲失败", j1.Error?.Message);
                    return null;
                }

                return j1.Value?.Songs;
            }, cancellationToken: CancellationToken.None);

            AppendNcSongs([.. rst.Select(t => t.MapNcSong())], false);
            return true;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSource时发生错误", ex.Message);
        }

        return false;
    }

    private async Task<bool> AppendAlbumAsync(string albumId)
    {
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, albumId, async () =>
            {
                var json = await _api.RequestAsync(NeteaseApis.AlbumApi,
                    new AlbumRequest { Id = albumId });
                if (json.IsError)
                {
                    _notification.ShowMessage("获取专辑信息失败", json.Error?.Message);
                    return null;
                }

                return json.Value;
            }, cancellationToken: CancellationToken.None);

            if (rst is null)
                return false;

            AppendNcSongs(rst.Songs?.Select(t => t.MapToNcSong()).ToList(), false);
            return true;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return false;
    }

    // ────────────── 内部辅助 ──────────────

    /// <summary>
    /// 构建播放策略上下文
    /// </summary>
    private PlayStrategyContext BuildStrategyContext()
    {
        lock (_lock)
        {
            return new PlayStrategyContext
            {
                Items = _items.AsReadOnly(),
                CurrentIndex = _nowPlayingIndex,
                CurrentItem = NowPlayingItem
            };
        }
    }

    /// <summary>
    /// 构建过渡策略上下文，将回调绑定到本服务
    /// </summary>
    private TrackTransitionContext BuildTransitionContext() => new()
    {
        Position = _state.Position,
        Duration = _state.Duration,
        CurrentItem = NowPlayingItem,
        RequestNextItemAsync = RequestNextItemAsync,
        CommitItemAsync = CommitItemAsync,
        LoadMediaSourceAsync = (item, primary, autoPlay, removeCurrentSongs) =>
            _control.LoadAndPlayAsync(item, primary, autoPlay, removeCurrentSongs: removeCurrentSongs),
        Player = _player
    };

    /// <summary>
    /// 供过渡策略回调：获取下一首曲目但不改变播放索引
    /// </summary>
    private Task<HyPlayItem?> RequestNextItemAsync(bool advance)
    {
        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null)
            return Task.FromResult<HyPlayItem?>(null);

        lock (_lock)
        {
            if (advance)
            {
                _nowPlayingIndex = nextIndex.Value;
                SyncIndex();
            }

            return Task.FromResult<HyPlayItem?>(_items[nextIndex.Value]);
        }
    }

    /// <summary>
    /// 供过渡策略回调：将预加载曲目提交为当前播放曲目。
    /// </summary>
    private Task CommitItemAsync(HyPlayItem item)
    {
        lock (_lock)
        {
            var index = _items.IndexOf(item);
            if (index >= 0)
            {
                _nowPlayingIndex = index;
                SyncIndex();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步内部索引到 <see cref="PlaybackStateService"/>
    /// </summary>
    private void SyncIndex()
    {
        _state.NowPlayingIndex = _nowPlayingIndex;
        _state.NowPlayingItem = NowPlayingItem;
    }

    /// <summary>
    /// 发送播放列表变更消息
    /// </summary>
    private static void SendPlaylistChanged(bool isShuffleTrigger = false)
    {
        WeakReferenceMessenger.Default.Send(new PlaylistChangedMessage(isShuffleTrigger));
    }

    // ────────────── Shuffle / 本地文件 / 通知 ──────────────

    /// <inheritdoc />
    public List<int> ShuffleList { get; } = [];

    /// <inheritdoc />
    public int ShufflingIndex { get; set; } = -1;

    /// <inheritdoc />
    public StorageFile? NowPlayingStorageFile { get; private set; }

    /// <inheritdoc />
    public async Task PickLocalFileAsync()
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

        foreach (var file in files)
        {
            try
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
                    using var stream = await file.OpenStreamForReadAsync();
                    if (NCMFile.IsCorrectNCMFile(stream))
                    {
                        var info = NCMFile.GetNCMMusicInfo(stream);
                        var hyitem = new HyPlayItem
                        {
                            ItemType = HyPlayItemType.Netease,
                            Album = new NCAlbum
                            {
                                Name = info.album,
                                Id = info.albumId.ToString(),
                                Cover = info.albumPic
                            },
                            LocalStorageFile = file,
                            Url = file.Path,
                            SubExt = info.format,
                            Bitrate = info.bitrate,
                            IsLocalFile = true,
                            LengthInMilliseconds = info.duration,
                            Id = info.musicId.ToString(),
                            TrackId = -1,
                            CDName = "01",
                            Artist = null,
                            Name = info.musicName,
                            InfoTag = file.Provider.DisplayName + " NCM"
                        };
                        hyitem.Artist = [.. info.artist.Select(t => new NCArtist
                        { Name = t[0].ToString(), Id = t[1].ToString() })];
                        lock (_lock) { _items.Add(hyitem); }
                    }
                }
                else
                {
                    var item = await LoadStorageFileAsync(file);
                    lock (_lock) { _items.Add(item); }
                }
            }
            catch (Exception ex)
            {
                _notification.ShowMessage($"加载文件 {file.Name} 失败", ex.Message);
            }
        }

        NotifyAppendDone();
        HyPlayItem? lastItem;
        lock (_lock) { lastItem = _items.LastOrDefault(); }
        if (lastItem != null)
            await MoveToAsync(lastItem);
    }

    /// <inheritdoc />
    public async Task<HyPlayItem> LoadStorageFileAsync(StorageFile sf, bool nocheck163 = false)
    {
        using var abstraction = new UwpStorageFileAbstraction(sf);
        using var tagFile = TagLibHelper.Create(abstraction, sf.FileType);
        if (nocheck163 || !The163KeyHelper.TryGetMusicInfo(tagFile.Tag, out var mi))
        {
            var songPerformersList = tagFile.Tag.Performers
                .Select(t => new NCArtist { Name = t, Type = HyPlayItemType.Local }).ToList();
            if (songPerformersList.Count == 0)
                songPerformersList.Add(new NCArtist { Name = "未知歌手", Type = HyPlayItemType.Local });

            var hyPlayItem = new HyPlayItem
            {
                IsLocalFile = true,
                LocalFileTag = tagFile.Tag,
                Bitrate = tagFile.Properties.AudioBitrate,
                InfoTag = sf.Provider.DisplayName,
                Id = null,
                Name = tagFile.Tag.Title,
                Artist = songPerformersList,
                Album = new NCAlbum { Name = tagFile.Tag.Album },
                TrackId = (int)tagFile.Tag.Track,
                CDName = "01",
                Url = sf.Path,
                SubExt = sf.FileType,
                Size = 0,
                LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds,
                ItemType = HyPlayItemType.Local
            };
            if (sf.Provider.Id == "network" || _setting.safeFileAccess)
                hyPlayItem.LocalStorageFile = sf;
            return hyPlayItem;
        }

        if (string.IsNullOrEmpty(mi.musicName))
            return await LoadStorageFileAsync(sf, true);

        return new HyPlayItem
        {
            ItemType = HyPlayItemType.Local,
            Album = new NCAlbum { Name = mi.album, Id = mi.albumId.ToString(), Cover = mi.albumPic },
            Url = sf.Path,
            SubExt = sf.FileType,
            LocalFileTag = tagFile.Tag,
            Bitrate = mi.bitrate,
            IsLocalFile = true,
            LengthInMilliseconds = tagFile.Properties.Duration.TotalMilliseconds,
            Id = mi.musicId.ToString(),
            Artist = [.. mi.artist.Select(t => new NCArtist { Name = t[0].ToString(), Id = t[1].ToString() })],
            Name = mi.musicName,
            LocalStorageFile = sf,
            TrackId = (int)tagFile.Tag.Track,
            CDName = "01",
            InfoTag = sf.Provider.DisplayName
        };
    }

    /// <inheritdoc />
    public Task CreateShufflePlayLists(string currentSongId = "-1")
    {
        ShuffleList.Clear();
        ShufflingIndex = 0;
        lock (_lock)
        {
            if (_items.Count != 0)
            {
                HashSet<int> shuffledNumbers = [];
                bool hasSpecifiedCorrectCurrentSong = false;
                if (currentSongId != "-1")
                {
                    int playItemIndex = _items.FindIndex(s => s.ToNCSong().SongId == currentSongId);
                    if (playItemIndex != -1)
                    {
                        shuffledNumbers.Add(playItemIndex);
                        ShuffleList.Add(playItemIndex);
                        hasSpecifiedCorrectCurrentSong = true;
                    }
                }

                while (shuffledNumbers.Count < _items.Count)
                {
                    var buffer = Guid.NewGuid().ToByteArray();
                    var seed = BitConverter.ToInt32(buffer, 0);
                    var random = new Random(seed);
                    var indexShuffled = random.Next(_items.Count);
                    if (shuffledNumbers.Add(indexShuffled))
                        ShuffleList.Add(indexShuffled);
                }

                ShufflingIndex = hasSpecifiedCorrectCurrentSong ? 0 : ShuffleList.IndexOf(_nowPlayingIndex);
            }
        }

        SendPlaylistChanged(true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void ReverseList()
    {
        lock (_lock)
        {
            _items.Reverse();
            if (_nowPlayingIndex >= 0 && _nowPlayingIndex < _items.Count)
                _nowPlayingIndex = _items.Count - _nowPlayingIndex - 1;
            SyncIndex();
        }
    }

    /// <inheritdoc />
    public void NotifyPlayItemChanged(HyPlayItem item)
    {
        WeakReferenceMessenger.Default.Send(new TrackChangedMessage(item));
    }

    /// <summary>
    /// Disposes the internal <see cref="CancellationTokenSource"/> used for track-end handling.
    /// </summary>
    public void Dispose()
    {
        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = null;
        _trackEndLock.Dispose();
    }
}
