using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using Windows.Devices.Input;
using Windows.Storage;
using HyPlayer.HyPlayControl;
using Newtonsoft.Json.Linq;

namespace HyPlayer.Classes
{
    public struct PureLyricInfo
    {
        public string PureLyrics;
        public string TrLyrics;
    }

    public struct SongLyric
    {
        public string PureLyric;
        public string Translation;
        public bool HaveTranslation;
        public TimeSpan LyricTime;

        public static SongLyric PureSong = new SongLyric()
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "纯音乐 请欣赏" };

        public static SongLyric NoLyric = new SongLyric()
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "无歌词 请欣赏" };

        public static SongLyric LoadingLyric = new SongLyric()
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "加载歌词中..." };
    }


    public class NCFmItem : NCSong
    {
        public string fmId;
        public string description;
        public string Radio;
    }
   
    public class NCSong
    {
        public HyPlayItemType Type;
        public string sid;
        public string songname;
        public List<NCArtist> Artist;
        public NCAlbum Album;
        public double LengthInMilliseconds;
        public int mvid;
        public string alias;
        public string transname;

        public static NCSong CreateFromJson(JToken song)
        {
            string alpath = "album";
            string arpath = "artists";
            string dtpath = "duration";
            if (song[alpath] == null)
                alpath = "al";
            if (song[arpath] == null)
                arpath = "ar";
            if (song[dtpath] == null)
                dtpath = "dt";
            NCSong NCSong = new NCSong()
            {
                Album = NCAlbum.CreateFormJson(song[alpath]),
                sid = song["id"].ToString(),
                songname = song["name"].ToString(),
                Artist = new List<NCArtist>(),
                LengthInMilliseconds = double.Parse(song[dtpath].ToString())
            };
            song[arpath].ToList().ForEach(t => { NCSong.Artist.Add(NCArtist.CreateFormJson(t)); });
            if (song["mv"] != null)
            {
                NCSong.mvid = song["mv"].ToObject<int>();
            }
            if (song["alia"] != null)
            {
                NCSong.alias = string.Join(" / ", song["alia"].ToArray().Select(t => t.ToString()));
            }
            if (song["tns"] != null)
                NCSong.transname = string.Join(" / ", song["tns"].ToArray().Select(t => t.ToString()));
            return NCSong;
        }
    }
    
    public struct NCPlayItem
    {
        public bool hasLocalFile;
        public StorageFile LocalStorageFile;
        public int bitrate;
        public string tag;
        public string id;
        public string songname;
        public HyPlayItemType Type;
        public List<NCArtist> Artist;
        public NCAlbum Album;
        public string url;
        public string subext;
        public string size;
        public string md5;
        public double LengthInMilliseconds;

        public NCSong ToNCSong()
        {
            return new NCSong()
            {
                Album = Album,
                Artist = Artist,
                LengthInMilliseconds = LengthInMilliseconds,
                sid = id,
                songname = songname
            };
        }
    }

    public struct NCPlayList
    {
        public string plid;
        public string cover;
        public string name;
        public string desc;
        public NCUser creater;
        public bool subscribed;
        public long trackCount;
        public long playCount;
        public long bookCount;

        public static NCPlayList CreateFromJson(JToken json)
        {
            try
            {
                string picpath = "picUrl";
                string descpath = "description";
                string subcountpath = "subscribedCount";
                string playcountpath = "playCount";
                if (json[picpath] == null)
                    picpath = "coverImgUrl";
                if (json[descpath] == null)
                    descpath = "copywriter";
                if (json[subcountpath] == null)
                    subcountpath = "bookCount";
                if (json[playcountpath] == null)
                {
                    playcountpath = "playcount";
                }
                NCPlayList ncp = new NCPlayList()
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
                if (json[subcountpath] != null)
                {
                    ncp.bookCount = json[subcountpath].ToObject<long>();
                }
                return ncp;
            }
            catch (Exception e)
            {
                return new NCPlayList();
            }

        }
    }

    public struct NCUser
    {
        public string id;
        public string name;
        public string avatar;
        public string signature;

        public static NCUser CreateFromJson(JToken user)
        {
            if (user != null && user.HasValues)
            {
                NCUser ncuser = new NCUser();
                if (user["avatarUrl"] != null)
                    ncuser.avatar = user["avatarUrl"].ToString();
                if (user["signature"] != null)
                {
                    ncuser.signature = user["signature"].ToString();
                }
                ncuser.id = user["userId"].ToString();
                ncuser.name = user["nickname"].ToString();
                return ncuser;
            }
            else
            {
                return new NCUser()
                {
                    avatar = "https://p1.music.126.net/KxePid7qTvt6V2iYVy-rYQ==/109951165050882728.jpg",
                    id = "1",
                    name = "网易云音乐",
                    signature = "网易云音乐官方帐号"
                };
            }
        }
    }

    public struct NCArtist
    {
        public string id;
        public string name;
        public string avatar;
        public string transname;
        public string alias;

        public static NCArtist CreateFormJson(JToken artist)
        {
            //TODO: 歌手这里尽量再来点信息
            var art = new NCArtist()
            {
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

    public struct NCAlbum
    {
        public bool isRealAlbum;
        public string id;
        public string name;
        public string cover;
        public string alias;
        public string description;

        public static NCAlbum CreateFormJson(JToken album)
        {
            return new NCAlbum()
            {
                isRealAlbum = true,
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

    public struct Comment
    {
        public string resourceId;
        public int resourceType;
        public string cid;
        public string uid;
        public Uri AvatarUri;
        public string Nickname;
        public string content;
        public bool HasLiked;
        public DateTime SendTime;
        public int likedCount;
        public bool IsByMyself => this.uid == Common.LoginedUser.id;
        public int ReplyCount;

        public static Comment CreateFromJson(JToken comment,string resourceId,int resourceType)
        {
            Comment cmt = new Comment();
            cmt.resourceId = resourceId;
            cmt.resourceType = resourceType;
            cmt.cid = comment["commentId"].ToString();
            cmt.SendTime =
                new DateTime((Convert.ToInt64(comment["time"].ToString()) * 10000) + 621355968000000000);
            cmt.AvatarUri = comment["user"]["avatarUrl"] is null
                ? new Uri("ms-appx:///Assets/icon.png")
                : new Uri(comment["user"]["avatarUrl"].ToString() + "?param=" +
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
}