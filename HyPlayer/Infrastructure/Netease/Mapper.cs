using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Converters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Infrastructure.Netease;

public static class Mapper
{
    public static NCSong ToNCSong(this SingleSongBase song)
    {
        var neteaseSong = song as NeteaseSong;
        return new NCSong
        {
            Album = song.Album is null
                ? new NCAlbum { AlbumType = HyPlayItemType.Netease, Id = string.Empty, Name = "未知专辑" }
                : song.Album.ToNCAlbum(),
            Alias = neteaseSong?.Alias is not null ? string.Join(",", neteaseSong.Alias) : null,
            Artist = GetArtists(song),
            CDName = neteaseSong?.CdName,
            IsCloud = false,
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            MVId = neteaseSong?.MvId,
            ProviderSong = song,
            SongId = song.ActualId,
            SongName = song.Name,
            TrackId = neteaseSong?.TrackNumber ?? 0,
            TranslatedName = neteaseSong?.Translation,
            IsAvailable = song.Available,
            Type = HyPlayItemType.Netease
        };
    }

    public static List<NCSong> ToNCSongs(this IEnumerable<SingleSongBase> songs)
    {
        return songs.Select(ToNCSong).ToList();
    }

    public static SingleSongBase ToSingleSong(this NCSong song)
    {
        return new NeteaseSong
        {
            ActualId = song.SongId,
            Name = song.SongName,
            Album = song.Album is null
                ? null
                : new NeteaseAlbum
                {
                    ActualId = song.Album.Id,
                    Name = song.Album.Name,
                    PictureUrl = song.Album.Cover,
                    Alias = string.IsNullOrWhiteSpace(song.Album.Alias) ? null : [song.Album.Alias]
                },
            Artists = song.Artist?.Select(artist => new NeteaseArtist
            {
                ActualId = artist.Id,
                Name = artist.Name
            }).Cast<PersonBase>().ToList(),
            CreatorList = song.Artist?.Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList(),
            Duration = (long)song.LengthInMilliseconds,
            Available = song.IsAvailable,
            Alias = string.IsNullOrWhiteSpace(song.Alias) ? null : song.Alias.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MvId = song.MVId,
            CdName = song.CDName,
            TrackNumber = song.TrackId,
            CoverUrl = song.Album?.Cover,
            Translation = song.TranslatedName
        };
    }

    public static SingleSongBase ToProviderSong(this NCSong song)
    {
        return song.ProviderSong ?? song.ToSingleSong();
    }

    public static NCAlbum ToNCAlbum(this ContainerBase album)
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Netease,
            Cover = album is NeteaseAlbum neteaseAlbum ? neteaseAlbum.PictureUrl : null,
            Id = album.ActualId,
            Name = album.Name
        };
    }

    private static List<NCArtist> GetArtists(SingleSongBase song)
    {
        if (song is NeteaseSong { Artists: { Count: > 0 } artists })
            return artists.Select(ToNCArtist).ToList();

        return song.CreatorList?.Select(name => new NCArtist
        {
            Name = name,
            Type = HyPlayItemType.Netease
        }).ToList() ?? [];
    }

    public static NCArtist ToNCArtist(this PersonBase artist)
    {
        return new NCArtist
        {
            Id = artist.ActualId,
            Name = artist.Name,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCSong MapToNcSong(this SongDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0" and not null,
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            MVId = song.MvId,
            SongId = song.Id,
            SongName = song.Name,
            TrackId = song.TrackNumber,
            TranslatedName = song.Translation,
            IsAvailable = true,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCSong MapToNcSong(this EmittedSongDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0" and not null,
            IsVip = song.Fee is 1,
            LengthInMilliseconds = song.Duration,
            MVId = song.MvId,
            SongId = song.Id,
            SongName = song.Name,
            TrackId = song.TrackNumber,
            TranslatedName = song.Translations is not null ? string.Join(",", song.Translations) : null,
            IsAvailable = true,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCMlog MapToNcMlog(this ArtistVideoResponse.ArtistVideoResponseData.ArtistVideoResponseDataRecord.ArtistVideoResponseResource.ArtistVideoResponseBaseData mlog)
    {
        return new NCMlog
        {
            Cover = mlog.CoverUrl,
            Id = mlog.Id,
            Title = mlog.Title,
            Duration = (int)mlog.Duration,
        };
    }

    public static Comment MapToComment(this CommentDto comment)
    {
        return new Comment
        {
            CommentId = comment.CommentId,
            Content = comment.Content,
            HasLiked = comment.Liked,
            LikedCount = comment.LikedCount,
            ReplyCount = comment.ReplyCount,
            SendTime = DateConverter.GetDateTimeFromTimeStamp(comment.Time),
            CommentUser = comment.User.MapToNcUser(),
        };
    }

    public static NCUser MapToNcUser(this UserInfoDto user)
    {
        return new NCUser
        {
            Avatar = user.AvatarUrl,
            Id = user.UserId,
            Name = user.Nickname,
            Signature = user.Signature
        };
    }

    public static NCSong MapNcSong(this EmittedSongDtoWithPrivilege song)
    {
        return new NCSong
        {
            Album = song.Album?.MapToNcAlbum() ?? new(),
            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0",
            IsVip = song.Fee is 1,
            LengthInMilliseconds = song.Duration,
            MVId = song.MvId,
            SongId = song.Id,
            SongName = song.Name,
            TrackId = song.TrackNumber,
            TranslatedName = song.Translations is not null ? string.Join(",", song.Translations) : null,
            IsAvailable = song.Privilege?.St is 0,
            Type = HyPlayItemType.Netease,
        };
    }
    public static NCSong MapNcSong(this ArtistSongDto song)
    {
        return new NCSong
        {
            Album = song.Album?.MapToNcAlbum() ?? new(),
            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            IsCloud = false,
            IsVip = song.Privilege.Fee is 1,
            LengthInMilliseconds = song.Duration,
            MVId = song.MvId,
            SongId = song.Id,
            SongName = song.Name,
            TrackId = song.TrackNumber,
            TranslatedName = song.Translations is not null ? string.Join(",", song.Translations) : null,
            IsAvailable = song.Privilege?.St is 0,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCSong MapToNcSong(this SongWithPrivilegeDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0",
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            MVId = song.MvId,
            SongId = song.Id,
            SongName = song.Name,
            TrackId = song.TrackNumber,
            TranslatedName = song.Translation,
            IsAvailable = song.Privilege?.St is 0,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCAlbum MapToNcAlbum(this AlbumDto album)
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Netease,
            Alias = album.Translation,
            Cover = album.PictureUrl,
            Description = album.Description,
            Id = album.Id,
            Name = album.Name
        };
    }

    public static NCArtist MapToNcArtist(this ArtistDto artist)
    {
        return new NCArtist
        {
            Alias = artist.Translation,
            Avatar = artist.Img1v1Url,
            Id = artist.Id,
            Name = artist.Name,
            TranslatedName = artist.Translation,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCArtist MapToNcArtist(this ArtistDetailResponse.ArtistDetailDto artist)
    {
        return new NCArtist
        {
            Alias = artist.Translation,
            Avatar = artist.Img1v1Url,
            Id = artist.Id,
            Name = artist.Name,
            TranslatedName = artist.Translation,
            Information = "歌曲数: " + artist.MusicSize + " | 专辑数: " + artist.AlbumSize + " | 视频数: " + artist.MvSize,
            Description = artist.BriefDesc,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCFmItem MapToNCFmItem(this DjRadioProgramDto dto)
    {
        return new NCFmItem
        {
            Type = HyPlayItemType.Radio,
            SongId = dto.MainSong?.Id,
            SongName = dto.Name,
            Artist = dto.Owner.MapToNCArtists(),
            Album = dto.Radio.MapToNcAlbum(),
            LengthInMilliseconds = dto.Duration,
            MVId = "-1",
            Alias = null,
            TranslatedName = null,
            FMId = dto.Id,
            Description = dto.Description,
            RadioId = dto.Radio?.Id,
            RadioName = dto.Radio?.Name
        };
    }


    public static NCRadio MapToNCRadio(this DjRadioChannelWithDjDto dto)
    {
        return new NCRadio
        {
            Cover = dto.CoverUrl,
            Description = dto.Description,
            DJ = dto.DjData.MapToNcUser(),
            Id = dto.Id,
            LastProgramName = dto.LastProgramName,
            Name = dto.Name,
            HasSubscribed = dto.Subscribed,
        };
    }

    public static List<NCArtist> MapToNCArtists(this UserInfoDto dto)
    {
        return
        [
            new NCArtist
            {
                Type = HyPlayItemType.Radio,
                Id = dto.UserId,
                Name = dto.Nickname,
                Avatar = dto.AvatarUrl
            }
        ];
    }

    public static NCAlbum MapToNcAlbum(this DjRadioChannelDto dto)
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Radio,
            Id = dto.Id,
            Name = dto.Name,
            Cover = dto.CoverUrl,
            Alias = dto.Id, //咱放在这个奇怪的位置
            Description = dto.Description
        };
    }
    public static NCPlayList MapToNCPlayList(this PlaylistDto dto)
    {
        var ncp = new NCPlayList
        {
            Cover = dto.CoverUrl,
            Creator = dto.Creator?.MapToNcUser() ?? new(),
            Description = dto.Description,
            Name = dto.Name,
            PlaylistId = dto.Id,
            HasSubscribed = dto.Subscribed,
            PlayCount = dto.PlayCount,
            TrackCount = dto.TrackCount,
            BookCount = dto.BookCount,
            UpdateTime = DateConverter.GetDateTimeFromTimeStamp(dto.UpdateTime)
        };
        return ncp;
    }
    public static NCPlayList MapToNCPlayList(this RecommendResourceResponse.RecommendResourceItem dto)
    {
        var ncp = new NCPlayList
        {
            Cover = dto.PicUrl,
            Creator = dto.Creator?.MapToNcUser() ?? new(),
            Description = dto.Description,
            Name = dto.Name,
            PlaylistId = dto.Id,
            HasSubscribed = dto.Subscribed,
            PlayCount = dto.PlayCount,
            TrackCount = dto.TrackCount,
            BookCount = dto.BookCount,
            UpdateTime = DateConverter.GetDateTimeFromTimeStamp(dto.UpdateTime)
        };
        return ncp;
    }
    public static NCPlayList MapToNCPlayList(this RecommendPlaylistDto dto)
    {
        var ncp = new NCPlayList
        {
            Cover = dto.CoverUrl,
            Creator = dto.Creator?.MapToNcUser() ?? new(),
            Name = dto.Name,
            PlaylistId = dto.Id,
            PlayCount = dto.PlayCount,
            TrackCount = dto.TrackCount
        };
        return ncp;
    }
    public static NCSong MapNCSong(this CloudMusicDto dto)
    {
        var song = new NCSong()
        {
            Album = dto.Song.Album.MapToNcAlbum(),
            SongName = dto.SongName,
            SongId = dto.SongId,
            IsAvailable = true,
            IsCloud = true,
            Artist = [new NCArtist() { Name = dto.Artist }],
            Type = HyPlayItemType.Netease
        };
        return song;
    }
}
