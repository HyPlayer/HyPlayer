using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 歌单源提供者 — 加载网易云歌单全部歌曲。
/// Prefix: "pl", Kind: <see cref="SongListQueueScopeKind.Playlist"/>
/// </summary>
internal sealed class PlaylistQueueSourceProvider : IQueueSourceProvider
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INotificationService _notification;

    public PlaylistQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)
    {
        _api = api;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Playlist;
    public string Prefix => QueueSourcePrefixes.Playlist;
    public bool SupportCompleteLoad => true;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var resp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracks, id, async () =>
            {
                var detailResponse = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,
                    new PlaylistTracksGetRequest { Id = id });
                if (detailResponse.IsError)
                {
                    _notification.ShowMessage("获取歌单失败", detailResponse.Error.Message);
                    return null;
                }

                return detailResponse.Value;
            }, cancellationToken: cancellationToken);

            var nowIndex = 0;
            var trackIds = resp?.Playlist?.TrackIds.Select(t => t.Id).ToList() ?? [];
            var batches = new List<IList<NCSong>>();
            while (nowIndex * 500 < trackIds.Count)
            {
                var nowIds = trackIds.GetRange(nowIndex * 500,
                    Math.Min(500, trackIds.Count - nowIndex * 500));
                var songDetailResp = await SimpleCacher.GetOrCreateCacheAsync(CacheType.PlaylistTracksDetail,
                    id + "_" + nowIndex, async () =>
                    {
                        var songResponse = await _api.RequestAsync(NeteaseApis.SongDetailApi,
                            new SongDetailRequest { IdList = nowIds });
                        if (songResponse.IsError)
                            _notification.ShowMessage("获取歌曲失败", songResponse.Error?.Message);
                        return songResponse.Value;
                    }, cancellationToken: cancellationToken);

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
}
