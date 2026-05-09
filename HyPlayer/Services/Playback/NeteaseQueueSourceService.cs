using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback;

/// <inheritdoc />
public sealed class NeteaseQueueSourceService : INeteaseQueueSourceService
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INotificationService _notification;

    public NeteaseQueueSourceService(NeteaseCloudMusicApiHandler api, INotificationService notification)
    {
        _api = api;
        _notification = notification;
    }

    /// <inheritdoc />
    public async Task<NeteaseQueueSourceLoadResult> LoadSourceAsync(string sourceId)
    {
        try
        {
            var prefix = sourceId[..2];
            switch (prefix)
            {
                case "pl":
                    return await LoadPlaylistAsync(sourceId[2..]);
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
                    return rst is not null
                        ? NeteaseQueueSourceLoadResult.FromSongs([rst.MapToNcSong()])
                        : NeteaseQueueSourceLoadResult.Failed;
                case "al":
                    return await LoadAlbumAsync(sourceId[2..]);
                case "sh":
                case "sa":
                    return await LoadSingerHotAsync(sourceId[2..]);
                case "rd":
                    return await LoadRadioListAsync(sourceId[2..]);
                default:
                    return NeteaseQueueSourceLoadResult.Failed;
            }
        }
        catch (Exception ex)
        {
            _notification.ShowMessage(ex.Message, (ex.InnerException ?? new Exception()).Message);
            return NeteaseQueueSourceLoadResult.Failed;
        }
    }

    /// <inheritdoc />
    public async Task<NeteaseQueueSourceLoadResult> LoadPlaylistAsync(string playlistId)
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
            var batches = new List<IList<NCSong>>();
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

                nowIndex++;
                if (songDetailResp?.Songs is { Length: > 0 } songs)
                    batches.Add(songs.Select(t => t.MapToNcSong()).ToList());
            }

            if (trackIds.Count > 0 && batches.Count == 0)
            {
                _notification.ShowMessage("获取歌单失败", "歌曲详情为空或全部获取失败");
                return NeteaseQueueSourceLoadResult.Failed;
            }

            return NeteaseQueueSourceLoadResult.FromBatches(batches);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendPlayList时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }

    /// <inheritdoc />
    public async Task<NeteaseQueueSourceLoadResult> LoadRadioListAsync(string radioId, bool asc = false)
    {
        try
        {
            bool? hasMore = true;
            var page = 0;
            var batches = new List<IList<NCSong>>();
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
                    return NeteaseQueueSourceLoadResult.Failed;
                }

                hasMore = json.Value is { Data.More: true };
                if (json.Value?.Data?.Programs is { Length: > 0 })
                    batches.Add([.. json.Value.Data.Programs.Select(t => (NCSong)t.MapToNCFmItem())]);

                page++;
            }

            return NeteaseQueueSourceLoadResult.FromBatches(batches);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendRadioList时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }

    /// <inheritdoc />
    public async Task<NeteaseQueueSourceLoadResult> LoadSingerHotAsync(string id)
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

            return rst is { Length: > 0 }
                ? NeteaseQueueSourceLoadResult.FromSongs([.. rst.Select(t => t.MapNcSong())])
                : NeteaseQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendNCSource时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }

    /// <inheritdoc />
    public async Task<NeteaseQueueSourceLoadResult> LoadAlbumAsync(string albumId)
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
                return NeteaseQueueSourceLoadResult.Failed;

            return NeteaseQueueSourceLoadResult.FromSongs(rst.Songs?.Select(t => t.MapToNcSong()).ToList() ?? []);
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("AppendAlbum时发生错误", ex.Message);
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}
