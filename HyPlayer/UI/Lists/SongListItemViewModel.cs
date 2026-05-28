using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public NeteaseAlbum Album { get; private init; }
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
    public bool IsRadio { get; private init; }
    public int DisplayOrder => Order + 1;

    public Uri? Cover => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(Album?.PictureUrl) ? DefaultCoverUrl : Album.PictureUrl) + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER);

    public string? CoverString => Ioc.Default.GetRequiredService<Setting>().noImage
        ? null
        : new Uri((string.IsNullOrEmpty(Album?.PictureUrl) ? DefaultCoverUrl : Album.PictureUrl) + "?param=" + StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString => Artist.Count == 0 ? string.Empty : string.Join(" / ", Artist.Select(t => t.Name));

    public string ConvertTranslate(string source) => string.IsNullOrEmpty(source) ? string.Empty : "(" + source + ")";

    public SingleSongBase ToProviderSong()
    {
        if (ProviderSong != null)
            return ProviderSong;

        return new NeteaseSong
        {
            ActualId = SongId,
            Name = SongName,
            Album = string.IsNullOrWhiteSpace(Album?.ActualId) && string.IsNullOrWhiteSpace(Album?.Name)
                ? null
                : new NeteaseAlbum
                {
                    ActualId = Album.ActualId,
                    Name = Album.Name,
                    PictureUrl = Album.PictureUrl,
                    Alias = Album.Alias
                },
            Artists = Artist.Select(artist => new NeteaseArtist
            {
                ActualId = artist.ActualId,
                Name = artist.Name
            }).Cast<PersonBase>().ToList(),
            CreatorList = Artist.Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList(),
            Duration = (long)LengthInMilliseconds,
            Available = IsAvailable,
            Alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MvId = MVId,
            CdName = CDName,
            TrackNumber = TrackId,
            CoverUrl = Album?.PictureUrl,
            Translation = TranslatedName
        };
    }

    public static async Task<SongListItemViewModel> FromProviderSongAsync(SingleSongBase song, int order, bool isCloud = false)
    {
        ArgumentNullException.ThrowIfNull(song);

        if (song is NeteaseRadioProgram radioProgram)
            return FromRadioProgram(radioProgram, order, isCloud);

        var creators = await song.GetCreatorsAsync();
        var neteaseSong = song as NeteaseSong;
        return new SongListItemViewModel
        {
            Album = new NeteaseAlbum
            {
                PictureUrl = neteaseSong?.CoverUrl,
                ActualId = song.Album?.ActualId,
                Name = song.Album?.Name
            },
            Alias = neteaseSong?.Alias is not null ? string.Join(",", neteaseSong.Alias) : null,
            Artist = creators?.Select(artist => new NeteaseArtist
                     {
                         ActualId = artist.ActualId,
                         Name = artist.Name,
                     }).Cast<PersonBase>().ToList() ?? [],
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
            ProviderId = song.ProviderId,
            IsRadio = false,
        };
    }

    public static SongListItemViewModel FromRadioProgram(NeteaseRadioProgram program, int order, bool isCloud = false)
    {
        ArgumentNullException.ThrowIfNull(program);

        return new SongListItemViewModel
        {
            Album = new NeteaseAlbum
            {
                ActualId = program.RadioChannel?.ActualId,
                Name = program.RadioChannel?.Name,
                PictureUrl = program.RadioChannel?.CoverUrl,
                Alias = program.RadioChannel?.ActualId is not null ? [program.RadioChannel.ActualId] : null,
                Description = program.RadioChannel?.Description
            },
            Alias = string.Empty,
            Artist = GetRadioArtists(program),
            CDName = string.Empty,
            IsCloud = isCloud,
            IsVip = false,
            LengthInMilliseconds = program.Duration,
            MVId = program.MainSong?.MvId ?? "-1",
            Order = order,
            SongId = program.MainSong?.ActualId ?? program.ActualId,
            SongName = program.Name,
            ProviderSong = program,
            TrackId = order + 1,
            TranslatedName = string.Empty,
            IsAvailable = program.Available,
            ProviderId = program.ProviderId,
            IsRadio = true,
        };
    }

    private static List<PersonBase> GetRadioArtists(NeteaseRadioProgram program)
    {
        if (program.Host is not null)
        {
            return
            [
                new NeteaseUser
                {
                    ActualId = program.Host.ActualId,
                    Name = program.Host.Name,
                    AvatarUrl = program.Host.AvatarUrl
                }
            ];
        }

        return program.MainSong?.Artists?.Select(artist => new NeteaseArtist
        {
            ActualId = artist.ActualId,
            Name = artist.Name
        }).Cast<PersonBase>().ToList() ?? [];
    }

    public static SongListItemViewModel FromFallback(string? id, string? name, int order, bool isCloud = false)
    {
        return new SongListItemViewModel
        {
            Album = new NeteaseAlbum { ActualId = string.Empty, Name = string.Empty, PictureUrl = string.Empty },
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
            ProviderId = "ncm",
            IsRadio = false,
        };
    }
}

public sealed class SongListItemGroup(IEnumerable<SongListItemViewModel> items) : List<SongListItemViewModel>(items)
{
    public string Key { get; set; } = string.Empty;
}
