using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using TagLib;

namespace HyPlayer.Platform.Playback.LocalProvider;

public sealed class LocalSong : SingleSongBase, IHasTranslation
{
    public override string ProviderId => LocalProvider.ProviderIdValue;

    public override string TypeId => IsNcm ? LocalProvider.LocalNcmSongTypeId : LocalProvider.LocalSongTypeId;

    public StorageFile? StorageFile { get; init; }

    public Tag? FileTag { get; init; }

    public int Bitrate { get; init; }

    public string? ExtensionName { get; init; }

    public string? InfoTag { get; init; }

    public int TrackNumber { get; init; }

    public string? CdName { get; init; }

    public bool IsNcm { get; init; }

    public IReadOnlyList<PersonBase>? Artists { get; init; }

    public string ArtistText => CreatorList is { Count: > 0 } creators
        ? string.Join("; ", creators)
        : "未知歌手";

    public string? Translation { get; set; }

    public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default)
    {
        return Task.FromResult(Artists is null ? null : new List<PersonBase>(Artists));
    }
}