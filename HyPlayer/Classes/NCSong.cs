#region

using ALRC.Abstraction;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.NeteaseApi.Models;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TagLib;
using Windows.Storage;
using Windows.Storage.Streams;

#endregion

namespace HyPlayer.Classes;
/// <summary>
/// 资源类型ID定义类
/// </summary>
public static class NeteaseTypeIds
{
    /// <summary>
    /// 单曲
    /// </summary>
    public const string SingleSong = "sg";

    /// <summary>
    /// 歌单
    /// </summary>
    public const string Playlist = "pl";

    /// <summary>
    /// 专辑
    /// </summary>
    public const string Artist = "ar";

    /// <summary>
    /// 歌手
    /// </summary>
    public const string Album = "al";

    /// <summary>
    /// 用户
    /// </summary>
    public const string User = "us";

    /// <summary>
    /// 电台节目
    /// </summary>
    public const string RadioProgram = "pr";

    /// <summary>
    /// 电台播客
    /// </summary>
    public const string RadioChannel = "dj";

    /// <summary>
    /// MV
    /// </summary>
    public const string Mv = "mv";

    /// <summary>
    /// MBlog
    /// </summary>
    public const string MBlog = "mb";

    /// <summary>
    /// 搜索结果
    /// </summary>
    public const string SearchResult = "sr";

    /// <summary>
    /// 方法歌曲容器
    /// </summary>
    public const string ActionGettableSongContainer = "ag";

    /// <summary>
    /// 排行榜
    /// </summary>
    public const string Chart = "ct";

    /// <summary>
    /// 动态
    /// </summary>
    public const string Dynamic = "dy";

    /// <summary>
    /// 歌单分类
    /// </summary>
    public const string PlaylistCategory = "PC";

    /// <summary>
    /// 歌词
    /// </summary>
    public const string Lyric = "lr";

    /// <summary>
    /// 私人 FM
    /// </summary>
    public const string PersonalFm = "fm";
}


public class HyPlayItem
{
    public HyPlayItemType ItemType;
    public PlayItem PlayItem;

    public NCSong ToNCSong()
    {
        if (PlayItem != null)
            return PlayItem.ToNCSong();
        return new NCSong();
    }
}

public class HyPlayerItemComparer : IEqualityComparer<HyPlayItem>
{
    public bool Equals(HyPlayItem x, HyPlayItem y)
    {
        return x?.ToNCSong().SongId == y?.ToNCSong().SongId;
    }

    public int GetHashCode(HyPlayItem obj)
    {
        return obj.ToNCSong().SongId.GetHashCode();
    }
}

public enum HyPlayItemType
{
    Local,
    LocalProgressive,
    Netease,
    Radio
}

public class KaraokLyricInfo : PureLyricInfo
{
    public string KaraokLyric { get; set; }
    public string YrTrLyrics { get; set; }
    public string YrNeteaseRomaji { get; set; }
}

public class HyALRCLyricInfo : PureLyricInfo
{
    public ALRCFile ALRC { get; set; }
}

[JsonDerivedType(typeof(HyALRCLyricInfo), "ALRC")]
[JsonDerivedType(typeof(KaraokLyricInfo), "Karaok")]
public class PureLyricInfo
{
    public string PureLyrics { get; set; }
    public string TrLyrics { get; set; }
    public string NeteaseRomaji { get; set; }
    public List<LyricInfoMetadata> SongMetadata { get; set; } = [];
    public List<LyricInfoMetadata> LyricMetadata { get; set; } = [];
}

public class SongLyric
{
    public static SongLyric PureSong = new()
    { LyricLine = new LrcLyricsLine("纯音乐 请欣赏", TimeSpan.Zero) };

    public static SongLyric NoLyric = new()
    { LyricLine = new LrcLyricsLine("无歌词 请欣赏", TimeSpan.Zero) };

    public static SongLyric LoadingLyric = new()
    { LyricLine = new LrcLyricsLine("加载歌词中...", TimeSpan.Zero) };

    public LyricLine LyricLine { get; set; }
    public string Translation { get; set; }
    public string Romaji { get; set; }

    public bool HaveTranslation => !string.IsNullOrEmpty(Translation);
    public bool HaveRomaji => !string.IsNullOrEmpty(Romaji);
}

public class NCRadio
{
    public string Cover { get; set; }
    public string Description { get; set; }
    public NCUser DJ { get; set; }
    public string Id { get; set; }
    public string LastProgramName { get; set; }
    public string Name { get; set; }
    public bool HasSubscribed { get; set; }
}

public class NCFmItem : NCSong
{
    public string Description { get; set; }
    public string FMId { get; set; }
    public string RadioId { get; set; }
    public string RadioName { get; set; }
}

public class NCSong
{
    public NCAlbum Album { get; set; }
    public string Alias { get; set; }
    public List<NCArtist> Artist { get; set; }
    public string CDName { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsCloud { get; set; }
    public bool IsVip { get; set; }

    public double LengthInMilliseconds;

    public string MVId { get; set; }
    public int Order { get; set; } = 0;
    public string SongId { get; set; }
    public string SongName { get; set; }
    public int TrackId { get; set; } = -1;
    public string TranslatedName { get; set; }
    public HyPlayItemType Type { get; set; }
    public int DisplayOrder => Order + 1;


    public Uri Cover =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.Cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_SINGLENCSONG_COVER);
    public string CoverString =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.Cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.Name)); }
    }


    public string ConvertTranslate(string source)
    {
        return string.IsNullOrEmpty(source) ? "" : "(" + source + ")";
    }
}

public class NCAlbumSong : NCSong
{
    public string DiscName { get; set; }
}

public class SimpleListItem
{
    public bool CanPlay { get; set; }
    public string CoverLink { get; set; }
    public string LineOne { get; set; }
    public string LineThree { get; set; }
    public string LineTwo { get; set; }
    public int Order { get; set; } = 0;
    public string ResourceId { get; set; }
    public string Title { get; set; }

    public Uri CoverUri =>
        Common.Setting.noImage
            ? null
            : new Uri((string.IsNullOrEmpty(CoverLink)
                          ? "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg"
                          : CoverLink) +
                      "?param=" +
                      StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM);

    public int DisplayOrder => Order + 1;
}

public class PlayItem
{
    public NCAlbum Album { get; set; }
    public List<NCArtist> Artist { get; set; }
    public int Bitrate { get; set; }
    public string CDName { get; set; }
    public string Translation { get; set; }
    public Tag LocalFileTag { get; set; }
    public StorageFile LocalStorageFile { get; set; } //如非特殊原因请不要设置这个东西!
    public string Id { get; set; }
    public bool IsLocalFile { get; set; }
    public double LengthInMilliseconds { get; set; }
    public string Name { get; set; }
    public long Size { get; set; }
    public string SubExt { get; set; }
    public string QualityTag { get; set; }
    public string InfoTag { get; set; }
    public int TrackId { get; set; }
    public HyPlayItemType Type { get; set; }
    public string Url { get; set; }
    public double Volume { get; set; } = 1d;
    public AudioGraphPlaybackSource AudioGraphPlaybackSource { get; set; }
    public InMemoryRandomAccessStream NcmPlayableStream { get; set; }
    public string NcmPlayableStreamMIMEType { get; set; } = string.Empty;

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.Name)); }
    }

    public string AlbumString => Album.Name ?? "未知专辑";

    public NCSong ToNCSong()
    {
        return new NCSong
        {
            Type = Type,
            Album = Album,
            Artist = Artist,
            LengthInMilliseconds = LengthInMilliseconds,
            SongId = Id,
            SongName = Name,
            TrackId = TrackId
        };
    }
    public void FreePlaybackResources()
    {
        AudioGraphPlaybackSource?.Dispose();
        NcmPlayableStream?.Dispose();
        NcmPlayableStreamMIMEType = null;
        AudioGraphPlaybackSource = null;
        NcmPlayableStream = null;
    }
}

public class NCPlayList
{
    public long BookCount { get; set; }
    public string Cover { get; set; }
    public NCUser Creator { get; set; }
    public string Description { get; set; }
    public string Name { get; set; }
    public long PlayCount { get; set; }
    public string PlaylistId { get; set; }
    public bool HasSubscribed { get; set; }
    public long TrackCount { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime UpdateTime { get; set; }

}

public class NCUser
{
    public string Avatar { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string Signature { get; set; }
}

public class NCMlog
{
    public string Cover { get; set; }
    public string Description { get; set; }
    public int duration { get; set; }
    public string Id { get; set; }
    public string title { get; set; }
}

public class NCArtist
{
    public string Alias { get; set; }
    public string Avatar { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
    public string TranslatedName { get; set; }
    public string Description { get; set; }
    public string Information { get; set; }
    public HyPlayItemType Type { get; set; }
}

public class NCAlbum
{
    public HyPlayItemType AlbumType { get; set; }
    public string Alias { get; set; }
    public string Cover { get; set; }
    public string Description { get; set; }
    public string Id { get; set; }
    public string Name { get; set; }
}

public class Comment
{
    public Comment thisComment => this; //绑定回去用
    public string CommentId { get; set; }
    public string Content { get; set; }
    public bool HasLiked { get; set; }
    public bool IsMainComment { get; set; } = true;
    public int LikedCount { get; set; }
    public int ReplyCount { get; set; }
    public string ResourceId { get; set; }
    public NeteaseResourceType ResourceType { get; set; }
    public DateTime SendTime { get; set; }
    public NCUser CommentUser { get; set; }
    public bool IsByMyself => CommentUser.Id == Common.LoginedUser?.Id;
}