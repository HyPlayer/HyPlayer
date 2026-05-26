using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

/// <summary>
/// Provider-backed song row model for list XAML migration.
/// It intentionally mirrors the current <see cref="NCSong"/> binding surface so pages can move one template at a time.
/// </summary>
public sealed class SongListItemViewModel
{
    private const string DefaultCoverUrl = "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg";
    private readonly NCSong? _sourceSong;

    private SongListItemViewModel()
    {
    }

    public SongListItemViewModel(NCSong song)
    {
        ArgumentNullException.ThrowIfNull(song);
        _sourceSong = song;

        Album = song.Album ?? new NCAlbum { Id = string.Empty, Name = "未知专辑", Cover = string.Empty };
        Alias = song.Alias ?? string.Empty;
        Artist = song.Artist ?? [];
        CDName = song.CDName ?? string.Empty;
        IsAvailable = song.IsAvailable;
        IsCloud = song.IsCloud;
        IsVip = song.IsVip;
        LengthInMilliseconds = song.LengthInMilliseconds;
        MVId = song.MVId ?? string.Empty;
        Order = song.Order;
        SongId = song.SongId ?? string.Empty;
        SongName = song.SongName ?? string.Empty;
        ProviderSong = song.ProviderSong;
        TrackId = song.TrackId;
        TranslatedName = song.TranslatedName ?? string.Empty;
        Type = song.Type;
    }

    public NCSong SourceSong => _sourceSong ?? ToNCSong();

    public NCAlbum Album { get; private init; }
    public string Alias { get; private init; }
    public List<NCArtist> Artist { get; private init; }
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
    public HyPlayItemType Type { get; private init; }
    public int DisplayOrder => Order + 1;

    public Uri? Cover => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(Album.Cover) ? DefaultCoverUrl : Album.Cover) + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER);

    public string? CoverString => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(Album.Cover) ? DefaultCoverUrl : Album.Cover) + "?param=" + StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString => Artist.Count == 0 ? string.Empty : string.Join(" / ", Artist.Select(t => t.Name));

    public string ConvertTranslate(string source) => string.IsNullOrEmpty(source) ? string.Empty : "(" + source + ")";

    public NCSong ToNCSong()
    {
        if (_sourceSong != null)
            return _sourceSong;

        return new NCSong
        {
            Album = Album,
            Alias = Alias,
            Artist = Artist,
            CDName = CDName,
            IsAvailable = IsAvailable,
            IsCloud = IsCloud,
            IsVip = IsVip,
            LengthInMilliseconds = LengthInMilliseconds,
            MVId = MVId,
            Order = Order,
            SongId = SongId,
            SongName = SongName,
            ProviderSong = ProviderSong,
            TrackId = TrackId,
            TranslatedName = TranslatedName,
            Type = Type
        };
    }

    public static SongListItemViewModel FromNCSong(NCSong song) => new(song);

    public static async Task<SongListItemViewModel> FromProviderSongAsync(SingleSongBase song, int order, bool isCloud = false)
    {
        ArgumentNullException.ThrowIfNull(song);

        var creators = await song.GetCreatorsAsync();
        var neteaseSong = song as NeteaseSong;
        return new SongListItemViewModel
        {
            Album = new NCAlbum
            {
                AlbumType = HyPlayItemType.Netease,
                Cover = neteaseSong?.CoverUrl,
                Id = song.Album?.ActualId,
                Name = song.Album?.Name
            },
            Alias = neteaseSong?.Alias is not null ? string.Join(",", neteaseSong.Alias) : null,
            Artist = creators?.Select(artist => new NCArtist
                     {
                         Id = artist.ActualId,
                         Name = artist.Name,
                         Type = HyPlayItemType.Netease
                     }).ToList() ?? [],
            CDName = neteaseSong?.CdName,
            IsCloud = isCloud,
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            MVId = neteaseSong?.MvId,
            Order = order,
            SongId = song.ActualId,
            SongName = song.Name,
            ProviderSong = song,
            TrackId = neteaseSong?.TrackNumber ?? 0,
            TranslatedName = neteaseSong?.Translation,
            IsAvailable = song.Available,
            Type = HyPlayItemType.Netease,
        };
    }

    public static SongListItemViewModel FromFallback(string? id, string? name, int order, bool isCloud = false)
    {
        return new SongListItemViewModel
        {
            Album = new NCAlbum { Id = string.Empty, Name = string.Empty, Cover = string.Empty, AlbumType = HyPlayItemType.Netease },
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
            Type = HyPlayItemType.Netease,
        };
    }
}

public sealed class SongListItemGroup(IEnumerable<SongListItemViewModel> items) : List<SongListItemViewModel>(items)
{
    public string Key { get; set; } = string.Empty;
}
