using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 电台源提供者 — 加载网易云电台/播客全部节目。
/// Prefix: "rd", Kind: <see cref="SongListQueueScopeKind.Radio"/>
/// </summary>
internal sealed class RadioQueueSourceProvider : IQueueSourceProvider
{
    private readonly NeteaseCloudMusicApiHandler _api;
    private readonly INotificationService _notification;

    public RadioQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)
    {
        _api = api;
        _notification = notification;
    }

    public SongListQueueScopeKind Kind => SongListQueueScopeKind.Radio;
    public string Prefix => QueueSourcePrefixes.Radio;
    public bool SupportCompleteLoad => true;

    public async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default)
        => await LoadAsync(id, asc: false, cancellationToken);

    /// <summary>内部重载 — 支持 asc 排序方向（由 <see cref="NeteaseQueueSourceService"/> 调用）</summary>
    internal async Task<NeteaseQueueSourceLoadResult> LoadAsync(string id, bool asc, CancellationToken cancellationToken = default)
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
                        RadioId = id,
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
}
