using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

/// <summary>
/// Provider-backed song row model for list XAML migration.
/// </summary>
public sealed class SongListItemViewModel
{
    private const string DefaultCoverUrl = "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg";
    private SongListItemViewModel()
    {
    }

    public AlbumBase Album { get; private init; }
    public string Alias { get; private init; }
    public List<PersonBase> Artist { get; private init; }
    public string CDName { get; private init; }
    public bool IsAvailable { get; private init; }
    public bool IsCloud { get; private init; }
    public bool IsVip { get; private init; }
    public double LengthInMilliseconds { get; private init; }
    public string MVId { get; private init; }
    public int Order { get; private init; }
    public string SongId { get; private init; }
    public string SongName { get; private init; }
    public SingleSongBase? ProviderSong { get; private init; }
    public int TrackId { get; private init; }
    public string TranslatedName { get; private init; }
    public string ProviderId { get; private init; }
    public string TypeId { get; private init; }
    public string CoverUrl { get; private init; }
    public bool IsRadio { get; private init; }
    public int DisplayOrder => Order + 1;

    public Uri? Cover => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(CoverUrl) ? DefaultCoverUrl : CoverUrl) + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER);

    public string? CoverString => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(CoverUrl) ? DefaultCoverUrl : CoverUrl) + "?param=" + StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString => Artist.Count == 0 ? string.Empty : string.Join(" / ", Artist.Select(t => t.Name));

    public string ConvertTranslate(string source) => string.IsNullOrEmpty(source) ? string.Empty : "(" + source + ")";

    public SingleSongBase ToProviderSong()
    {
        if (ProviderSong != null)
            return ProviderSong;

        return new ProviderSongSnapshot
        {
            ActualId = SongId,
            Name = SongName,
            ProviderIdValue = ProviderId,
            TypeIdValue = TypeId,
            Album = Album,
            Artists = Artist,
            CreatorList = Artist.Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList(),
            Duration = (long)LengthInMilliseconds,
            Available = IsAvailable,
            AliasList = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            RichMediaIdValue = MVId,
            DiscNameValue = CDName,
            TrackNumberValue = TrackId,
            CoverUrl = CoverUrl,
            Translation = TranslatedName
        };
    }

    public static async Task<SongListItemViewModel> FromProviderSongAsync(SingleSongBase song, int order, bool isCloud = false)
    {
        return await FromProviderSongAsync(song, order, isCloud, false);
    }

    private static async Task<SongListItemViewModel> FromProviderSongAsync(SingleSongBase song, int order, bool isCloud, bool isRadio)
    {
        ArgumentNullException.ThrowIfNull(song);

        var creators = await song.GetCreatorsAsync();
        var coverUrl = await TryGetCoverUrlAsync(song);
        if (string.IsNullOrWhiteSpace(coverUrl))
            coverUrl = await TryGetCoverUrlAsync(song.Album);
        var aliases = song is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
        var track = song as IHasTrackMetadata;
        var richMedia = song as IHasRichMediaReference;
        return new SongListItemViewModel
        {
            Album = song.Album ?? CreateAlbumSnapshot(song.ProviderId, GetKnownTypeIds().AlbumTypeId, string.Empty, string.Empty, coverUrl),
            Alias = aliases is not null ? string.Join(",", aliases) : null,
            Artist = creators ?? [],
            CDName = track?.DiscName,
            IsCloud = isCloud,
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            MVId = richMedia?.RichMediaId,
            Order = order,
            SongId = song.ActualId,
            SongName = song.Name,
            ProviderSong = song,
            TrackId = track?.TrackNumber ?? (isRadio ? order + 1 : 0),
            TranslatedName = song is IHasTranslation translation ? translation.Translation : null,
            IsAvailable = song.Available,
            ProviderId = song.ProviderId,
            TypeId = song.TypeId,
            CoverUrl = coverUrl,
            IsRadio = isRadio,
        };
    }

    public static async Task<SongListItemViewModel> FromRadioProgramAsync(SingleSongBase program, int order, bool isCloud = false)
    {
        return await FromProviderSongAsync(program, order, isCloud, true);
    }

    public static SongListItemViewModel FromFallback(string? id, string? name, int order, bool isCloud = false)
    {
        var knownTypeIds = GetKnownTypeIds();
        return new SongListItemViewModel
        {
            Album = CreateAlbumSnapshot(knownTypeIds.Id, knownTypeIds.AlbumTypeId, string.Empty, string.Empty, string.Empty),
            Alias = string.Empty,
            Artist = [],
            CDName = string.Empty,
            IsAvailable = true,
            IsCloud = isCloud,
            IsVip = false,
            LengthInMilliseconds = 0,
            MVId = string.Empty,
            Order = order,
            SongId = id ?? string.Empty,
            SongName = name ?? string.Empty,
            ProviderSong = null,
            TrackId = 0,
            TranslatedName = string.Empty,
            ProviderId = knownTypeIds.Id,
            TypeId = knownTypeIds.SingleSongTypeId,
            CoverUrl = string.Empty,
            IsRadio = false,
        };
    }

    private static IProviderKnownTypeIds GetKnownTypeIds()
    {
        return Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    }

    private static ProviderAlbumSnapshot CreateAlbumSnapshot(string providerId, string typeId, string actualId, string name, string? coverUrl)
    {
        return new ProviderAlbumSnapshot
        {
            ProviderIdValue = providerId,
            TypeIdValue = typeId,
            ActualId = actualId,
            Name = name,
            CoverUrl = coverUrl
        };
    }

    private static async Task<string?> TryGetCoverUrlAsync(object? item)
    {
        if (item is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult
            ? (await uriResult.GetResourceAsync())?.GetLeftPart(UriPartial.Path)
            : null;
    }

    private sealed class ProviderSongSnapshot : SingleSongBase, IHasAliases, IHasCover, IHasRichMediaReference, IHasTrackMetadata, IHasTranslation
    {
        public required string ProviderIdValue { get; init; }
        public required string TypeIdValue { get; init; }
        public IReadOnlyList<string>? AliasList { get; init; }
        public required List<PersonBase> Artists { get; init; }
        public string? CoverUrl { get; init; }
        public string? RichMediaIdValue { get; init; }
        public string? DiscNameValue { get; init; }
        public int TrackNumberValue { get; init; }
        public string? Translation { get; set; }
        public override string ProviderId => ProviderIdValue;
        public override string TypeId => TypeIdValue;
        public IReadOnlyList<string>? Aliases => AliasList;
        public string? RichMediaId => RichMediaIdValue;
        public string? DiscName => DiscNameValue;
        public int TrackNumber => TrackNumberValue;

        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default)
        {
            return Task.FromResult<List<PersonBase>?>(Artists);
        }

        public Task<ResourceResultBase> GetCoverAsync(ImageResourceQualityTag? qualityTag = null, CancellationToken ctk = default)
        {
            return Task.FromResult<ResourceResultBase>(new ProviderImageResourceResult
            {
                ExternalException = null,
                ResourceStatus = string.IsNullOrWhiteSpace(CoverUrl) ? ResourceStatus.Fail : ResourceStatus.Success,
                Uri = string.IsNullOrWhiteSpace(CoverUrl) ? null : new Uri(CoverUrl)
            });
        }
    }

    private sealed class ProviderAlbumSnapshot : AlbumBase, IHasCover
    {
        public required string ProviderIdValue { get; init; }
        public required string TypeIdValue { get; init; }
        public string? CoverUrl { get; init; }
        public override string ProviderId => ProviderIdValue;
        public override string TypeId => TypeIdValue;

        public Task<ResourceResultBase> GetCoverAsync(ImageResourceQualityTag? qualityTag = null, CancellationToken ctk = default)
        {
            return Task.FromResult<ResourceResultBase>(new ProviderImageResourceResult
            {
                ExternalException = null,
                ResourceStatus = string.IsNullOrWhiteSpace(CoverUrl) ? ResourceStatus.Fail : ResourceStatus.Success,
                Uri = string.IsNullOrWhiteSpace(CoverUrl) ? null : new Uri(CoverUrl)
            });
        }
    }

    private sealed class ProviderImageResourceResult : ResourceResultBase, IResourceResultOf<Uri?>
    {
        public override Exception? ExternalException { get; init; }
        public override required ResourceStatus ResourceStatus { get; init; }
        public Uri? Uri { get; init; }

        public Task<Uri?> GetResourceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Uri);
        }
    }
}

public sealed class SongListItemGroup(IEnumerable<SongListItemViewModel> items) : List<SongListItemViewModel>(items)
{
    public string Key { get; set; } = string.Empty;
}
