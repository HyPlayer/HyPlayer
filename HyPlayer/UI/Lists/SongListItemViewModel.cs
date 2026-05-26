using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.UI.Lists;

/// <summary>
/// Provider-backed song row model for list XAML migration.
/// It intentionally mirrors the current <see cref="NCSong"/> binding surface so pages can move one template at a time.
/// </summary>
public sealed class SongListItemViewModel
{
    private const string DefaultCoverUrl = "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg";
    private readonly NCSong? _sourceSong;

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

    public NCAlbum Album { get; }
    public string Alias { get; }
    public List<NCArtist> Artist { get; }
    public string CDName { get; }
    public bool IsAvailable { get; }
    public bool IsCloud { get; }
    public bool IsVip { get; }
    public double LengthInMilliseconds { get; }
    public string MVId { get; }
    public int Order { get; }
    public string SongId { get; }
    public string SongName { get; }
    public SingleSongBase? ProviderSong { get; }
    public int TrackId { get; }
    public string TranslatedName { get; }
    public HyPlayItemType Type { get; }
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
}

public sealed class SongListItemGroup(IEnumerable<SongListItemViewModel> items) : List<SongListItemViewModel>(items)
{
    public string Key { get; set; } = string.Empty;
}
