using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface IPlaybackQueueLoader
{
    Task<bool> AppendNcSourceAsync(string sourceId);
    Task<bool> AppendSourceByKindAsync(SongListQueueScopeKind kind, string id);
    Task<bool> AppendRadioListAsync(string radioId, bool asc = false);
    Task<bool> AppendSongsAsync(IEnumerable<SingleSongBase> songs, bool skipDuplicateSingle = false);
}
