using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.QueueProviders;

/// <summary>
/// 电台源提供者 — 加载网易云电台/播客全部节目。
/// Prefix: "rd", Kind: <see cref="SongListQueueScopeKind.Radio"/>
/// </summary>
internal sealed class RadioQueueSourceProvider : IQueueSourceProvider
{
    private readonly IProvidableItemProvidable _provider;
    private readonly INotificationService _notification;

    public RadioQueueSourceProvider(IProvidableItemProvidable provider, INotificationService notification)
    {
        _provider = provider;
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
            var radio = await _provider.GetProvidableItemByIdAsync("dj" + id, cancellationToken);
            if (radio is not IProgressiveLoadingContainer container)
                return NeteaseQueueSourceLoadResult.Failed;

            var hasMore = true;
            var offset = 0;
            var batches = new List<IList<NCSong>>();
            const int count = 100;
            while (hasMore)
            {
                var result = await container.GetProgressiveItemsListAsync(offset, count, cancellationToken);
                hasMore = result.Item1;
                var songs = result.Item2.OfType<SingleSongBase>().Select(song => song.ToHyPlayItem().ToNCSong()).ToList();
                if (asc)
                    songs.Reverse();
                if (songs.Count > 0)
                    batches.Add(songs);

                offset += count;
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
