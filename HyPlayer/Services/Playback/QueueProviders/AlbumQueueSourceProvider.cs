using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 专辑源提供者 — 加载网易云专辑全部歌曲。
/// Prefix: "al", Kind: <see cref="SongListQueueScopeKind.Album"/>
/// </summary>
internal sealed class AlbumQueueSourceProvider : IQueueSourceProvider
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly ITeachingTipService _teachingTipService;

    public AlbumQueueSourceProvider(NeteaseCloudMusicApiHandler api, ITeachingTipService teachingTipService)
    {
        _api = api;
        _teachingTipService = teachingTipService;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Album;
    public string Prefix => QueueSourcePrefixes.Album;
    public bool SupportCompleteLoad => true;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, id, async () =>
            {
                var json = await _api.RequestAsync(NeteaseApis.AlbumApi,
                    new AlbumRequest { Id = id });
                if (json.IsError)
                {
                    _teachingTipService.Items.Enqueue(new("获取专辑信息失败", json.Error?.Message));
                    return null;
                }

                return json.Value;
            }, cancellationToken: cancellationToken);

            if (rst is null)
                return NeteaseQueueSourceLoadResult.Failed;

            return NeteaseQueueSourceLoadResult.FromSongs(rst.Songs?.Select(t => t.MapToNcSong()).ToList() ?? []);
        }
        catch (Exception ex)
        {
            _teachingTipService.Items.Enqueue(new("AppendAlbum时发生错误", ex.Message));
        }

        return NeteaseQueueSourceLoadResult.Failed;
    }
}
