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

    public static NCArtist ToNCArtist(this PersonBase artist)
    {
        return new NCArtist
        {
            Id = artist.ActualId,
            Name = artist.Name,
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
}
