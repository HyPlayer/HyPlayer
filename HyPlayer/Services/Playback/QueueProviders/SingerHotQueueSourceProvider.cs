using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 歌手热门歌曲源提供者 — 加载网易云歌手热门 Top 歌曲。
/// Prefix: "sa" (也兼容 "sh"), Kind: <see cref="SongListQueueScopeKind.Artist"/>
/// </summary>
internal sealed class SingerHotQueueSourceProvider : IQueueSourceProvider
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly ITeachingTipService _teachingTipService;

    public SingerHotQueueSourceProvider(NeteaseCloudMusicApiHandler api, ITeachingTipService teachingTipService)
    {
        _api = api;
        _teachingTipService = teachingTipService;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Artist;
    public string Prefix => QueueSourcePrefixes.Singer;
    public bool SupportCompleteLoad => false;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, id, async () =>
            {
                var j1 = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,
                    new ArtistTopSongRequest { ArtistId = id });
                if (j1.IsError)
                {
                    _teachingTipService.Items.Enqueue(new("获取歌手热门歌曲失败", j1.Error?.Message));
                    return null;
                }

                return j1.Value?.Songs;
            }, cancellationToken: cancellationToken);

            return rst is { Length: > 0 }
                ? NeteaseQueueSourceLoadResult.FromSongs([.. rst.Select(t => t.MapNcSong())])
                : NeteaseQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _teachingTipService.Items.Enqueue(new("AppendNCSource时发生错误", ex.Message));
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}
