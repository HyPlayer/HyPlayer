using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Domain.Music;

/// <summary>
/// TEMPORARY ADAPTER: Converts between PlayCore provider item abstractions and the legacy <see cref="HyPlayItem" /> model.
/// Remove when all UI/viewmodels use provider item abstractions directly and PlaylistService no longer stores HyPlayItem as its internal canonical model.
/// </summary>
public static class HyPlayItemCompatibilityAdapter
{
    private const string NeteaseProviderId = "ncm";
    private const string NeteaseSongTypeId = "sg";
    private const string NeteaseRadioTypeId = "dj";
    private const string LocalProviderId = "lcl";
    private const string LocalSongTypeId = "sg";
    private const string LocalNcmSongTypeId = "ncm";

    private static readonly ConditionalWeakTable<HyPlayItem, HyPlayItemProviderMetadata> ProviderMetadata = new();

    /// <summary>
    /// Converts a PlayCore provider item to the legacy <see cref="HyPlayItem" /> shape used by current UI and playlist code.
    /// </summary>
    /// <param name="item">The PlayCore provider item to adapt.</param>
    /// <returns>A legacy play item carrying the provider item identity.</returns>
    public static HyPlayItem ToHyPlayItem(this ProvidableItemBase item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item is SingleSongBase song
            ? song.ToHyPlayItem()
            : CreateBaseHyPlayItem(item, null);
    }

    /// <summary>
    /// Converts a PlayCore single-song item to the legacy <see cref="HyPlayItem" /> shape used by current UI and playlist code.
    /// </summary>
    /// <param name="song">The PlayCore song to adapt.</param>
    /// <returns>A legacy play item carrying song, album, creator, cover, and duration data when available.</returns>
    public static HyPlayItem ToHyPlayItem(this SingleSongBase song)
    {
        ArgumentNullException.ThrowIfNull(song);

        var metadata = new HyPlayItemProviderMetadata(
            GetIdentity(song),
            song.Album is null ? null : GetIdentity(song.Album),
            GetCompletedCreators(song)?.Select(GetIdentity).ToList(),
            GetCompletedCoverUri(song)?.ToString());

        var playItem = CreateBaseHyPlayItem(song, metadata);
        playItem.LengthInMilliseconds = song.Duration;
        playItem.Album = ToLegacyAlbum(song.Album, metadata.CoverIdentity);
        playItem.Artist = ToLegacyArtists(song, metadata.CreatorIdentities);
        playItem.Translation = song is IHasTranslation translatedSong ? translatedSong.Translation ?? string.Empty : string.Empty;
        playItem.CDName = string.Empty;
        playItem.TrackId = 0;
        return playItem;
    }

    /// <summary>
    /// Converts a PlayCore single-song item to <see cref="HyPlayItem" /> while awaiting optional creators and cover providers.
    /// </summary>
    /// <param name="song">The PlayCore song to adapt.</param>
    /// <param name="cancellationToken">A token that cancels optional provider metadata loading.</param>
    /// <returns>A legacy play item carrying song, album, creator, cover, and duration data when available.</returns>
    public static async Task<HyPlayItem> ToHyPlayItemAsync(this SingleSongBase song, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(song);

        var creators = await song.GetCreatorsAsync(cancellationToken).ConfigureAwait(false);
        var coverUri = await GetCoverUriAsync(song, cancellationToken).ConfigureAwait(false);
        var metadata = new HyPlayItemProviderMetadata(
            GetIdentity(song),
            song.Album is null ? null : GetIdentity(song.Album),
            creators?.Select(GetIdentity).ToList(),
            coverUri?.ToString());

        var playItem = CreateBaseHyPlayItem(song, metadata);
        playItem.LengthInMilliseconds = song.Duration;
        playItem.Album = ToLegacyAlbum(song.Album, metadata.CoverIdentity);
        playItem.Artist = ToLegacyArtists(song, creators, metadata.CreatorIdentities);
        playItem.Translation = song is IHasTranslation translatedSong ? translatedSong.Translation ?? string.Empty : string.Empty;
        playItem.CDName = string.Empty;
        playItem.TrackId = 0;
        return playItem;
    }

    /// <summary>
    /// Gets the PlayCore provider identity carried by a legacy <see cref="HyPlayItem" />.
    /// </summary>
    /// <param name="item">The legacy play item to inspect.</param>
    /// <returns>The provider id, type id, and actual id for provider-facing operations.</returns>
    public static (string ProviderId, string TypeId, string ActualId) GetItemIdentity(this HyPlayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (ProviderMetadata.TryGetValue(item, out var metadata))
        {
            return metadata.ItemIdentity;
        }

        return (GetFallbackProviderId(item), GetFallbackTypeId(item), GetFallbackActualId(item));
    }

    private static HyPlayItem CreateBaseHyPlayItem(ProvidableItemBase item, HyPlayItemProviderMetadata? metadata)
    {
        var playItem = new HyPlayItem
        {
            ItemType = GetFallbackItemType(item.ProviderId, item.TypeId),
            Id = item.ActualId ?? string.Empty,
            Name = item.Name ?? string.Empty,
            Album = new NCAlbum { AlbumType = GetFallbackItemType(item.ProviderId, item.TypeId), Id = string.Empty, Name = string.Empty, Cover = string.Empty },
            Artist = [],
            CDName = string.Empty,
            Translation = string.Empty,
            SubExt = string.Empty,
            QualityTag = string.Empty,
            InfoTag = string.Empty,
            Url = string.Empty
        };

        // TEMPORARY ADAPTER: Remove when all UI/viewmodels use provider item abstractions directly and PlaylistService no longer stores HyPlayItem as its internal canonical model.
        AttachMetadata(playItem, metadata ?? new HyPlayItemProviderMetadata(GetIdentity(item), null, null, null));
        return playItem;
    }

    private static NCAlbum ToLegacyAlbum(AlbumBase? album, string? coverIdentity)
    {
        if (album is null)
        {
            return new NCAlbum { AlbumType = HyPlayItemType.Netease, Id = string.Empty, Name = string.Empty, Cover = coverIdentity ?? string.Empty };
        }

        return new NCAlbum
        {
            AlbumType = GetFallbackItemType(album.ProviderId, album.TypeId),
            Id = album.ActualId ?? string.Empty,
            Name = album.Name ?? string.Empty,
            Cover = coverIdentity ?? string.Empty
        };
    }

    private static List<NCArtist> ToLegacyArtists(SingleSongBase song, IReadOnlyList<(string ProviderId, string TypeId, string ActualId)>? creatorIdentities)
    {
        var creators = GetCompletedCreators(song);
        return ToLegacyArtists(song, creators, creatorIdentities);
    }

    private static List<NCArtist> ToLegacyArtists(SingleSongBase song, IReadOnlyList<PersonBase>? creators, IReadOnlyList<(string ProviderId, string TypeId, string ActualId)>? creatorIdentities)
    {
        if (creators is { Count: > 0 })
        {
            return creators.Select((creator, index) => ToLegacyArtist(creator, creatorIdentities, index)).ToList();
        }

        return song.CreatorList?.Select((creatorName, index) => new NCArtist
        {
            Name = creatorName,
            Id = creatorIdentities?.ElementAtOrDefault(index).ActualId ?? string.Empty,
            Type = HyPlayItemType.Netease
        }).ToList() ?? [];
    }

    private static NCArtist ToLegacyArtist(PersonBase creator, IReadOnlyList<(string ProviderId, string TypeId, string ActualId)>? creatorIdentities, int index)
    {
        var identity = creatorIdentities?.ElementAtOrDefault(index) ?? GetIdentity(creator);
        return new NCArtist
        {
            Id = identity.ActualId,
            Name = creator.Name ?? string.Empty,
            Type = GetFallbackItemType(identity.ProviderId, identity.TypeId)
        };
    }

    private static IReadOnlyList<PersonBase>? GetCompletedCreators(SingleSongBase song)
    {
        var creatorsTask = song.GetCreatorsAsync();
        return creatorsTask.IsCompletedSuccessfully ? creatorsTask.Result : null;
    }

    private static Uri? GetCompletedCoverUri(SingleSongBase song)
    {
        if (song is not IHasCover coverProvider) return null;

        var coverTask = coverProvider.GetCoverAsync();
        if (!coverTask.IsCompletedSuccessfully || coverTask.Result is not IResourceResultOf<Uri?> uriResult) return null;

        var uriTask = uriResult.GetResourceAsync();
        return uriTask.IsCompletedSuccessfully ? uriTask.Result : null;
    }

    private static async Task<Uri?> GetCoverUriAsync(SingleSongBase song, CancellationToken cancellationToken)
    {
        if (song is not IHasCover coverProvider) return null;

        var coverResult = await coverProvider.GetCoverAsync(null, cancellationToken).ConfigureAwait(false);
        if (coverResult is not IResourceResultOf<Uri?> uriResult) return null;

        return await uriResult.GetResourceAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (string ProviderId, string TypeId, string ActualId) GetIdentity(ProvidableItemBase item)
    {
        return (item.ProviderId, item.TypeId, item.ActualId ?? string.Empty);
    }

    private static HyPlayItemType GetFallbackItemType(string providerId, string typeId)
    {
        if (providerId == "lcl") return HyPlayItemType.Local;
        if (typeId is NeteaseRadioTypeId or "pr") return HyPlayItemType.Radio;
        return HyPlayItemType.Netease;
    }

    private static string GetFallbackProviderId(HyPlayItem item)
    {
        if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
            return LocalProviderId;

        return item.ItemType is HyPlayItemType.Netease or HyPlayItemType.Radio ? NeteaseProviderId : item.ProviderId;
    }

    private static string GetFallbackTypeId(HyPlayItem item)
    {
        if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            return IsLocalNcmFile(item) ? LocalNcmSongTypeId : LocalSongTypeId;
        }

        return item.ItemType switch
        {
            HyPlayItemType.Radio => NeteaseRadioTypeId,
            _ => NeteaseSongTypeId
        };
    }

    private static string GetFallbackActualId(HyPlayItem item)
    {
        if (item.IsLocalFile || item.ItemType is HyPlayItemType.Local or HyPlayItemType.LocalProgressive)
        {
            return item.LocalStorageFile?.Path
                   ?? item.Url
                   ?? item.Id
                   ?? string.Empty;
        }

        return item.Id ?? string.Empty;
    }

    private static bool IsLocalNcmFile(HyPlayItem item)
    {
        return string.Equals(item.LocalStorageFile?.FileType, ".ncm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(item.SubExt, ".ncm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetExtension(item.Url), ".ncm", StringComparison.OrdinalIgnoreCase);
    }

    private static void AttachMetadata(HyPlayItem playItem, HyPlayItemProviderMetadata metadata)
    {
        ProviderMetadata.Remove(playItem);
        ProviderMetadata.Add(playItem, metadata);
    }

    private sealed record HyPlayItemProviderMetadata(
        (string ProviderId, string TypeId, string ActualId) ItemIdentity,
        (string ProviderId, string TypeId, string ActualId)? AlbumIdentity,
        IReadOnlyList<(string ProviderId, string TypeId, string ActualId)>? CreatorIdentities,
        string? CoverIdentity);

    private sealed class LegacyImageResourceResult(Uri? uri) : ResourceResultBase, IResourceResultOf<Uri?>
    {
        public override Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
        public Task<Uri?> GetResourceAsync(CancellationToken cancellationToken = default) => Task.FromResult(uri);
    }
}



