using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Classes;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// Loads NetEase queue sources and maps them to NCSong batches.
/// </summary>
public interface INeteaseQueueSourceService
{
    Task<NeteaseQueueSourceLoadResult> LoadSourceAsync(string sourceId);

    Task<NeteaseQueueSourceLoadResult> LoadPlaylistAsync(string playlistId);

    Task<NeteaseQueueSourceLoadResult> LoadRadioListAsync(string radioId, bool asc = false);

    Task<NeteaseQueueSourceLoadResult> LoadSingerHotAsync(string id);

    Task<NeteaseQueueSourceLoadResult> LoadAlbumAsync(string albumId);
}

public sealed class NeteaseQueueSourceLoadResult
{
    public bool Success { get; init; }

    public IList<IList<NCSong>> Batches { get; init; } = [];

    public static NeteaseQueueSourceLoadResult Failed { get; } = new() { Success = false };

    public static NeteaseQueueSourceLoadResult FromSongs(IList<NCSong> songs) => new()
    {
        Success = true,
        Batches = songs is { Count: > 0 } ? [songs] : []
    };

    public static NeteaseQueueSourceLoadResult FromBatches(IList<IList<NCSong>> batches) => new()
    {
        Success = true,
        Batches = batches?.Where(batch => batch is { Count: > 0 }).ToList() ?? []
    };
}
