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
/// 等价于HyPlayer.PlayCore的同名类. 
/// </summary>
public class ProvidableItemBase
{

}

/// <summary>
/// 资源类型ID定义类.（啊没错，又是从NeteaseProvider里抄来的）
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
        return x?.ToNCSong().sid == y?.ToNCSong().sid;
    }

    public int GetHashCode(HyPlayItem obj)
    {
        return obj.ToNCSong().sid.GetHashCode();
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

public class ALRCLyricInfo : PureLyricInfo
{
    public ALRCFile ALRC { get; set; }
}

[JsonDerivedType(typeof(ALRCLyricInfo), "ALRC")]
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

public class NCRadio : ProvidableItemBase
{
    public string cover;
    public string desc;
    public NCUser DJ;
    public string id;
    public string lastProgramName;
    public string name;
    public bool subed;
}

public class NCFmItem : NCSong
{
    public string description;
    public string fmId;
    public string RadioId;
    public string RadioName;
}

public class NCSong : ProvidableItemBase
{
    public NCAlbum Album;
    public string alias;
    public List<NCArtist> Artist;
    public string CDName;
    public bool IsAvailable = true;
    public bool IsCloud;
    public bool IsVip;

    public double LengthInMilliseconds;

    public string mvid;
    public int Order = 0;
    public string sid;
    public string songname;
    public int TrackId = -1;
    public string transname;
    public HyPlayItemType Type;
    public int DspOrder => Order + 1;


    public Uri Cover =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_SINGLENCSONG_COVER);
    public string CoverString =>
        Common.Setting.noImage
            ? null
            : new Uri((Album.cover ??
                       "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg") +
                      "?param=" +
                      StaticSource.PICSIZE_HOME_CARD_COVER).ToString();

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.name)); }
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
    public bool CanPlay;
    public string CoverLink;
    public string LineOne;
    public string LineThree;
    public string LineTwo;
    public int Order = 0;
    public string ResourceId;
    public string Title;

    public Uri CoverUri =>
        Common.Setting.noImage
            ? null
            : new Uri((string.IsNullOrEmpty(CoverLink)
                          ? "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg"
                          : CoverLink) +
                      "?param=" +
                      StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM);

    public int DspOrder => Order + 1;
}

public class PlayItem
{
    public NCAlbum Album;
    public List<NCArtist> Artist;
    public int Bitrate;
    public string CDName;
    public string Translation;
    public Tag LocalFileTag;
    public StorageFile DontSetLocalStorageFile; //如非特殊原因请不要设置这个东西!
    public string Id;
    public bool IsLocalFile;
    public double LengthInMilliseconds;
    public string Name;
    public long Size;
    public string SubExt;
    public string QualityTag;
    public string InfoTag;
    public int TrackId;
    public HyPlayItemType Type;
    public string Url;
    public double Volume = 1d;
    public AudioGraphPlaybackSource AudioGraphPlaybackSource;
    public InMemoryRandomAccessStream NcmPlayableStream;
    public string NcmPlayableStreamMIMEType = string.Empty;

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.name)); }
    }

    public string AlbumString => Album.name ?? "未知专辑";

    public NCSong ToNCSong()
    {
        return new NCSong
        {
            Type = Type,
            Album = Album,
            Artist = Artist,
            LengthInMilliseconds = LengthInMilliseconds,
            sid = Id,
            songname = Name,
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

public class NCPlayList : ProvidableItemBase
{
    public long bookCount;
    public string cover;
    public NCUser creater;
    public string desc;
    public string name;
    public long playCount;
    public string plid;
    public bool subscribed;
    public long trackCount;
    public DateTime createTime;
    public DateTime updateTime;

}

public class NCUser : ProvidableItemBase
{
    public string avatar;
    public string id;
    public string name;
    public string signature;
}

public class NCMlog : ProvidableItemBase
{
    public string cover;
    public string description;
    public int duration;
    public string id;
    public string title;
}

public class NCArtist : ProvidableItemBase
{
    public string alias;
    public string avatar;
    public string id;
    public string name;
    public string transname;
    public HyPlayItemType Type;
}

public class NCAlbum : ProvidableItemBase
{
    public HyPlayItemType AlbumType;
    public string alias;
    public string cover;
    public string description;
    public string id;
    public string name;
}

public class Comment : ProvidableItemBase
{
    public Comment thisComment => this; //绑定回去用
    public string cid;
    public string content;
    public bool HasLiked;
    public bool IsMainComment = true;
    public int likedCount;
    public int ReplyCount;
    public string resourceId;
    public NeteaseResourceType resourceType;
    public DateTime SendTime;
    public NCUser CommentUser;
    public bool IsByMyself => CommentUser.id == Common.LoginedUser?.id;
}