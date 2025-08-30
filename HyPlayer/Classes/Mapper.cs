using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Classes;

public static class Mapper
{
    public static NCSong MapToNcSong(this SongDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0" and not null,
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            mvid = song.MvId,
            sid = song.Id,
            songname = song.Name,
            TrackId = song.TrackNumber,
            transname = song.Translation,
            IsAvailable = true,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCSong MapToNcSong(this EmittedSongDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0" and not null,
            IsVip = song.Fee is 1,
            LengthInMilliseconds = song.Duration,
            mvid = song.MvId,
            sid = song.Id,
            songname = song.Name,
            TrackId = song.TrackNumber,
            transname = song.Translations is not null ? string.Join(",", song.Translations) : null,
            IsAvailable = true,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCMlog MapToNcMlog(this ArtistVideoResponse.ArtistVideoResponseData.ArtistVideoResponseDataRecord.ArtistVideoResponseResource.ArtistVideoResponseBaseData mlog)
    {
        return new NCMlog
        {
            cover = mlog.CoverUrl,
            id = mlog.Id,
            title = mlog.Title,
            duration = (int)mlog.Duration,
        };
    }

    public static Comment MapToComment(this CommentDto comment)
    {
        return new Comment
        {
            cid = comment.CommentId,
            content = comment.Content,
            HasLiked = comment.Liked,
            likedCount = comment.LikedCount,
            ReplyCount = comment.ReplyCount,
            SendTime = DateConverter.GetDateTimeFromTimeStamp(comment.Time),
            CommentUser = comment.User.MapToNcUser(),
        };
    }

    public static NCUser MapToNcUser(this UserInfoDto user)
    {
        return new NCUser
        {
            avatar = user.AvatarUrl,
            id = user.UserId,
            name = user.Nickname,
            signature = user.Signature
        };
    }

    public static NCSong MapNcSong(this EmittedSongDtoWithPrivilege song)
    {
        return new NCSong
        {
            Album = song.Album?.MapToNcAlbum() ?? new(),
            alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0",
            IsVip = song.Fee is 1,
            LengthInMilliseconds = song.Duration,
            mvid = song.MvId,
            sid = song.Id,
            songname = song.Name,
            TrackId = song.TrackNumber,
            transname = song.Translations is not null ? string.Join(",", song.Translations) : null,
            IsAvailable = song.Privilege?.St is 0,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCSong MapToNcSong(this SongWithPrivilegeDto song)
    {
        return new NCSong
        {
            Album = song.Album.MapToNcAlbum(),
            alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                         .ToList() ??
                     [],
            CDName = song.CdName,
            IsCloud = song.Sid is not "0",
            IsVip = false,
            LengthInMilliseconds = song.Duration,
            mvid = song.MvId,
            sid = song.Id,
            songname = song.Name,
            TrackId = song.TrackNumber,
            transname = song.Translation,
            IsAvailable = song.Privilege?.St is 0,
            Type = HyPlayItemType.Netease,
        };
    }

    public static NCAlbum MapToNcAlbum(this AlbumDto album)
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Netease,
            alias = album.Translation,
            cover = album.PictureUrl,
            description = album.Description,
            id = album.Id,
            name = album.Name
        };
    }

    public static NCArtist MapToNcArtist(this ArtistDto artist)
    {
        return new NCArtist
        {
            alias = artist.Translation,
            avatar = artist.Img1v1Url,
            id = artist.Id,
            name = artist.Name,
            transname = artist.Translation,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCArtist MapToNcArtist(this ArtistDetailResponse.ArtistDetailDto artist)
    {
        return new NCArtist
        {
            alias = artist.Translation,
            avatar = artist.Img1v1Url,
            id = artist.Id,
            name = artist.Name,
            transname = artist.Translation,
            Type = HyPlayItemType.Netease
        };
    }

    public static NCFmItem MapToNCFmItem(this DjRadioProgramDto dto)
    {
        return new NCFmItem
        {
            Type = HyPlayItemType.Radio,
            sid = dto.MainSong?.Id,
            songname = dto.Name,
            Artist = dto.Owner.MapToNCArtists(),
            Album = dto.Radio.MapToNcAlbum(),
            LengthInMilliseconds = dto.Duration,
            mvid = "-1",
            alias = null,
            transname = null,
            fmId = dto.Id,
            description = dto.Description,
            RadioId = dto.Radio?.Id,
            RadioName = dto.Radio?.Name
        };
    }


    public static NCRadio MapToNCRadio(this DjRadioChannelWithDjDto dto)
    {
        return new NCRadio
        {
            cover = dto.CoverUrl,
            desc = dto.Description,
            DJ = dto.DjData.MapToNcUser(),
            id = dto.Id,
            lastProgramName = dto.LastProgramName,
            name = dto.Name,
            subed = dto.Subscribed,
        };
    }

    public static List<NCArtist> MapToNCArtists(this UserInfoDto dto)
    {
        return
        [
            new NCArtist
            {
                Type = HyPlayItemType.Radio,
                id = dto.UserId,
                name = dto.Nickname,
                avatar = dto.AvatarUrl
            }
        ];
    }

    public static NCAlbum MapToNcAlbum(this DjRadioChannelDto dto)
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Radio,
            id = dto.Id,
            name = dto.Name,
            cover = dto.CoverUrl,
            alias = dto.Id, //咱放在这个奇怪的位置
            description = dto.Description
        };
    }
    public static NCPlayList MapToNCPlayList(this PlaylistDto dto)
    {
        var ncp = new NCPlayList
        {
            cover = dto.CoverUrl,
            creater = dto.Creator?.MapToNcUser() ?? new(),
            desc = dto.Description,
            name = dto.Name,
            plid = dto.Id,
            subscribed = dto.Subscribed,
            playCount = dto.PlayCount,
            trackCount = dto.TrackCount,
            bookCount = dto.BookCount,
            updateTime = DateConverter.GetDateTimeFromTimeStamp(dto.UpdateTime)
        };
        return ncp;
    }
    public static NCPlayList MapToNCPlayList(this RecommendResourceResponse.RecommendResourceItem dto)
    {
        var ncp = new NCPlayList
        {
            cover = dto.PicUrl,
            creater = dto.Creator?.MapToNcUser() ?? new(),
            desc = dto.Description,
            name = dto.Name,
            plid = dto.Id,
            subscribed = dto.Subscribed,
            playCount = dto.PlayCount,
            trackCount = dto.TrackCount,
            bookCount = dto.BookCount,
            updateTime = DateConverter.GetDateTimeFromTimeStamp(dto.UpdateTime)
        };
        return ncp;
    }
    public static NCPlayList MapToNCPlayList(this RecommendPlaylistDto dto)
    {
        var ncp = new NCPlayList
        {
            cover = dto.CoverUrl,
            creater = dto.Creator?.MapToNcUser() ?? new(),
            name = dto.Name,
            plid = dto.Id,
            playCount = dto.PlayCount,
            trackCount = dto.TrackCount
        };
        return ncp;
    }
}