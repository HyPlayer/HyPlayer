using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 单曲源提供者 — 加载单首网易云歌曲详情。
/// Prefix: "ns", Kind: <see cref="SongListQueueScopeKind.SingleSong"/>
/// </summary>
internal sealed class SingleSongQueueSourceProvider : IQueueSourceProvider
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly ITeachingTipService _teachingTipService;

    public SingleSongQueueSourceProvider(NeteaseCloudMusicApiHandler api, ITeachingTipService teachingTipService)
    {
        _api = api;
        _teachingTipService = teachingTipService;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.SingleSong;
    public string Prefix => QueueSourcePrefixes.SingleSong;
    public bool SupportCompleteLoad => false;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.SongDetail,
                string.Concat("ncm", id.AsSpan()),
                async () =>
                {
                    var result = await _api.RequestAsync(NeteaseApis.SongDetailApi,
                        new SongDetailRequest { Id = id });
                    if (result.IsError)
                    {
                        _teachingTipService.Enqueue(new("获取歌曲信息失败", result.Error?.Message));
                        return null;
                    }

                    if (result.Value?.Songs is not { Length: > 0 })
                    {
                        _teachingTipService.Enqueue(new("获取歌曲信息失败", "歌曲信息为空"));
                        return null;
                    }

                    return result.Value.Songs[0];
                }, cancellationToken: cancellationToken);

            return rst is not null
                ? NeteaseQueueSourceLoadResult.FromSongs([rst.MapToNcSong()])
                : NeteaseQueueSourceLoadResult.Failed;
        }
        catch (Exception ex)
        {
            _teachingTipService.Enqueue(new("获取歌曲信息失败", ex.Message));
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}
