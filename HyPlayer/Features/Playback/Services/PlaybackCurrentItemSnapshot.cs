using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Text.Json.Serialization;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
/// Provider-first projection for currently playing metadata used by UI surfaces.
/// </summary>
public sealed class PlaybackCurrentItemSnapshot
{
    public PlaybackCurrentItemSnapshot(
        string name,
        string translation,
        string artistText,
        string albumName,
        long duration,
        bool isLocal,
        SingleSongBase? providerItem)
    {
        Name = name;
        Translation = translation;
        ArtistText = artistText;
        AlbumName = albumName;
        Duration = duration;
        IsLocal = isLocal;
        ProviderItem = providerItem;
    }

    public string Name { get; }
    public string Translation { get; }
    public string ArtistText { get; }
    public string AlbumName { get; }
    public long Duration { get; }
    public bool IsLocal { get; }
    [JsonIgnore]
    public SingleSongBase? ProviderItem { get; }

    public static PlaybackCurrentItemSnapshot? FromProvider(SingleSongBase? item)
    {
        if (item is null)
            return null;

        return new PlaybackCurrentItemSnapshot(
            item.Name ?? string.Empty,
            item is IHasTranslation translated ? translated.Translation ?? string.Empty : string.Empty,
            item.CreatorList is { Count: > 0 } creators ? string.Join("; ", creators) : string.Empty,
            item.Album?.Name ?? string.Empty,
            item.Duration,
            IsLocalProviderItem(item),
            item);
    }

    private static bool IsLocalProviderItem(SingleSongBase item)
    {
        return item.ProviderId is "lcl";
    }
}
