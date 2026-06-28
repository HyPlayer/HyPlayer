using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface IPlaybackQueueLoader
{
    Task<ProviderQueueSourceLoadResult> LoadSourceByKindAsync(
        SongListQueueScopeKind kind,
        string id,
        CancellationToken cancellationToken = default);

    Task<bool> AppendNcSourceAsync(string sourceId, CancellationToken cancellationToken = default);
    Task<bool> AppendSourceByKindAsync(SongListQueueScopeKind kind, string id, CancellationToken cancellationToken = default);
    Task<bool> AppendRadioListAsync(string radioId, bool asc = false, CancellationToken cancellationToken = default);
    Task<bool> AppendSongsAsync(
        IEnumerable<SingleSongBase> songs,
        bool skipDuplicateSingle = false,
        CancellationToken cancellationToken = default);
}
