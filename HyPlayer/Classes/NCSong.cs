﻿#region

using LyricParser.Abstraction;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using TagLib;
using Windows.Storage;

#endregion

namespace HyPlayer.Classes;

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
    public string KaraokLyric;
}

public class PureLyricInfo
{
    public string PureLyrics;
    public string TrLyrics;
    public string NeteaseRomaji;
}

public class SongLyric
{
    public static SongLyric PureSong = new()
        { LyricLine = new LrcLyricsLine("纯音乐 请欣赏", TimeSpan.Zero) };

    public static SongLyric NoLyric = new()
        { LyricLine = new LrcLyricsLine("无歌词 请欣赏", TimeSpan.Zero) };

    public static SongLyric LoadingLyric = new()
        { LyricLine = new LrcLyricsLine("加载歌词中...", TimeSpan.Zero) };

    public ILyricLine LyricLine;
    public string Translation;
    public string Romaji;

    public bool HaveTranslation => !string.IsNullOrEmpty(Translation);
    public bool HaveRomaji => !string.IsNullOrEmpty(Romaji);
    public Type LyricType => LyricLine.GetType();
}

public class NCRadio
{
    public string cover;
    public string desc;
    public NCUser DJ;
    public string id;
    public string lastProgramName;
    public string name;
    public bool subed;

    public static NCRadio CreateFromJson(JToken json)
    {
        return new NCRadio
        {
            cover = json["picUrl"].ToString(),
            desc = json["desc"].ToString(),
            id = json["id"].ToString(),
            name = json["name"].ToString(),
            DJ = NCUser.CreateFromJson(json["dj"]),
            lastProgramName = json["lastProgramName"].ToString()
        };
    }

    public NCAlbum ConvertToNcAlbum()
    {
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Radio,
            id = "-1",
            name = name,
            cover = cover,
            alias = name,
            description = desc
        };
    }
}

public class NCFmItem : NCSong
{
    public string description;
    public string fmId;
    public string RadioId;
    public string RadioName;

    public new static NCFmItem CreateFromJson(JToken song)
    {
        return new NCFmItem
        {
            Type = HyPlayItemType.Radio,
            sid = song["mainTrackId"].ToString(),
            songname = song["name"].ToString(),
            Artist = new List<NCArtist>
            {
                new()
                {
                    Type = HyPlayItemType.Radio,
                    id = song["dj"]["userId"].ToString(),
                    name = song["dj"]["nickname"].ToString(),
                    avatar = song["dj"]["avatarUrl"].ToString()
                }
            },
            Album = new NCAlbum
            {
                AlbumType = HyPlayItemType.Radio,
                id = song["radio"]["id"].ToString(),
                name = song["radio"]["name"].ToString(),
                cover = song["coverUrl"].ToString(),
                alias = song["id"].ToString(), //咱放在这个奇怪的位置
                description = song["radio"]["desc"].ToString()
            },
            LengthInMilliseconds = song["duration"].ToObject<double>(),
            mvid = -1,
            alias = null,
            transname = null,
            fmId = song["id"].ToString(),
            description = song["description"].ToString(),
            RadioId = song["radio"]["id"].ToString(),
            RadioName = song["radio"]["name"].ToString()
        };
    }
}

public class NCSong
{
    public NCAlbum Album;
    public string alias;
    public List<NCArtist> Artist;
    public string CDName;
    public bool IsAvailable = true;
    public bool IsCloud;
    public bool IsVip;

    public double LengthInMilliseconds;

    public int mvid;
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

    public string ArtistString
    {
        get { return string.Join(" / ", Artist.Select(t => t.name)); }
    }

    public static NCSong CreateFromJson(JToken song)
    {
        if (song == null) return null;
        var alpath = "album";
        var arpath = "artists";
        var dtpath = "duration";
        if (song[alpath] == null)
            alpath = "al";
        if (song[arpath] == null)
            arpath = "ar";
        if (song[dtpath] == null)
            dtpath = "dt";
        var NCSong = new NCSong
        {
            IsVip = song["fee"]?.ToString() == "1",
            IsCloud = song["s_id"]?.ToString() != "0",
            Type = HyPlayItemType.Netease,
            Album = NCAlbum.CreateFromJson(song[alpath]),
            sid = song["id"].ToString(),
            TrackId = song["no"]?.ToObject<int>() ?? -1,
            songname = song["name"].ToString(),
            CDName = song["cd"]?.ToString() ?? "01",
            Artist = new List<NCArtist>(),
            LengthInMilliseconds = double.Parse(song[dtpath].ToString())
        };
        if (song[arpath].HasValues)
            song[arpath].ToList().ForEach(t => { NCSong.Artist.Add(NCArtist.CreateFromJson(t)); });
        else
            NCSong.Artist.Add(new NCArtist());
        if (song["mv"] != null) NCSong.mvid = song["mv"].ToObject<int>();

        if (song["alia"] != null)
            NCSong.alias = string.Join(" / ", song["alia"].ToArray().Select(t => t.ToString()));

        if (song["tns"] != null)
            NCSong.transname = string.Join(" / ", song["tns"].ToArray().Select(t => t.ToString()));
        if (song["privilege"] != null)
            NCSong.IsAvailable = song["privilege"]["st"].ToString() == "0";
        return NCSong;
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
    public Tag LocalFileTag;
    public StorageFile DontSetLocalStorageFile; //如非特殊原因请不要设置这个东西!
    public string Id;
    public bool IsLocalFile;
    public double LengthInMilliseconds;
    public string Name;
    public string Size;
    public string SubExt;
    public string Tag;
    public int TrackId;
    public HyPlayItemType Type;
    public string Url;

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
}

public class NCPlayList
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

    public static NCPlayList CreateFromJson(JToken json)
    {
        try
        {
            var picpath = "picUrl";
            var descpath = "description";
            var subcountpath = "subscribedCount";
            var playcountpath = "playCount";
            if (json[picpath] == null)
                picpath = "coverImgUrl";
            if (json[descpath] == null)
                descpath = "copywriter";
            if (json[subcountpath] == null)
                subcountpath = "bookCount";
            if (json[playcountpath] == null) playcountpath = "playcount";

            var ncp = new NCPlayList
            {
                cover = json[picpath].ToString(),
                creater = NCUser.CreateFromJson(json["creator"]),
                desc = json[descpath].ToString(),
                name = json["name"].ToString(),
                plid = json["id"].ToString(),
                subscribed = !(json["subscribed"] == null || json["subscribed"].ToString() == "False"),
                playCount = json[playcountpath].ToObject<long>(),
                trackCount = json["trackCount"].ToObject<long>(),
            };
            if (json["createTime"] != null)
                ncp.createTime = DateConverter.GetDateTimeFromTimeStamp(json["createTime"].ToObject<long>());
            if (json["updateTime"] != null)
                ncp.updateTime = DateConverter.GetDateTimeFromTimeStamp(json["updateTime"].ToObject<long>());

            if (json[subcountpath] != null) ncp.bookCount = json[subcountpath].ToObject<long>();

            return ncp;
        }
        catch
        {
            return new NCPlayList();
        }
    }
}

public class NCUser
{
    public string avatar;
    public string id;
    public string name;
    public string signature;

    public static NCUser CreateFromJson(JToken user)
    {
        if (user != null && user.HasValues)
        {
            var ncuser = new NCUser();
            if (user["avatarUrl"] != null)
                ncuser.avatar = user["avatarUrl"].ToString();
            if (user["signature"] != null) ncuser.signature = user["signature"].ToString();

            ncuser.id = user["userId"].ToString();
            ncuser.name = user["nickname"].ToString();
            return ncuser;
        }

        return new NCUser
        {
            avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
            id = "1",
            name = "网易云音乐",
            signature = "网易云音乐官方帐号"
        };
    }
}

public class NCMlog
{
    public string cover;
    public string description;
    public int duration;
    public string id;
    public string title;

    public static NCMlog CreateFromJson(JToken json)
    {
        return new NCMlog
        {
            id = json["id"].ToString(),
            title = json["text"].ToString(),
            description = json["desc"].ToString(),
            cover = json["coverUrl"].ToString(),
            duration = json["duration"].ToObject<int>()
        };
    }
}

public class NCArtist
{
    public string alias;
    public string avatar;
    public string id;
    public string name;
    public string transname;
    public HyPlayItemType Type;

    public static NCArtist CreateFromJson(JToken artist)
    {
        //TODO: 歌手这里尽量再来点信息
        var art = new NCArtist
        {
            Type = HyPlayItemType.Netease,
            id = artist["id"].ToString(),
            name = artist["name"].ToString()
        };
        if (artist["alias"] != null)
            art.alias = string.Join(" / ", artist["alias"].Select(t => t.ToString()).ToArray());
        if (artist["trans"] != null) art.transname = artist["trans"].ToString();
        if (artist["picUrl"] != null) art.avatar = artist["picUrl"].ToString();
        return art;
    }
}

public class NCAlbum
{
    public HyPlayItemType AlbumType;
    public string alias;
    public string cover;
    public string description;
    public string id;
    public string name;

    public static NCAlbum CreateFromJson(JToken album)
    {
        if (album?.HasValues is not true) return new NCAlbum();
        return new NCAlbum
        {
            AlbumType = HyPlayItemType.Netease,
            alias = album["alias"] != null
                ? string.Join(" / ", album["alias"].ToArray().Select(t => t.ToString()))
                : "",
            cover = album["picUrl"].ToString(),
            description = album["description"] != null ? album["description"].ToString() : "",
            id = album["id"].ToString(),
            name = album["name"].ToString()
        };
    }
}

public class Comment
{
    public Comment thisComment => this; //绑定回去用
    public Uri AvatarUri;
    public string cid;
    public string content;
    public bool HasLiked;
    public bool IsMainComment = true;
    public int likedCount;
    public string Nickname;
    public int ReplyCount;
    public string resourceId;
    public int resourceType;
    public DateTime SendTime;
    public string uid;
    public bool IsByMyself => uid == Common.LoginedUser?.id;

    public static Comment CreateFromJson(JToken comment, string resourceId, int resourceType)
    {
        var cmt = new Comment();
        cmt.resourceId = resourceId;
        cmt.resourceType = resourceType;
        cmt.cid = comment["commentId"].ToString();
        cmt.SendTime =
            new DateTime(Convert.ToInt64(comment["time"].ToString()) * 10000 + 621355968000000000);
        cmt.AvatarUri = comment["user"]["avatarUrl"] is null
            ? new Uri("ms-appx:///Assets/icon.png")
            : new Uri(comment["user"]["avatarUrl"] + "?param=" +
                      StaticSource.PICSIZE_COMMENTUSER_AVATAR);
        cmt.Nickname = comment["user"]["nickname"] is null
            ? comment["user"]["userId"].ToString()
            : comment["user"]["nickname"].ToString();
        cmt.uid = comment["user"]["userId"].ToString();
        cmt.content = comment["content"].ToString();
        cmt.likedCount = comment["likedCount"].ToObject<int>();
        if (comment["showFloorComment"].HasValues)
            cmt.ReplyCount = comment["showFloorComment"]["replyCount"].ToObject<int>();
        if (comment["liked"].ToString() == "False")
            cmt.HasLiked = false;
        else cmt.HasLiked = true;
        return cmt;
    }
}