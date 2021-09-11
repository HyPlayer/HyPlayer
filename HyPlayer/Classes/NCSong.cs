using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
using HyPlayer.HyPlayControl;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml.Media.Imaging;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public class HyPlayItem
    {
        public HyPlayItemType ItemType;
        public PlayItem PlayItem;

        public NCSong ToNCSong()
        {
            return PlayItem.ToNCSong();
        }
    }

    public enum HyPlayItemType
    {
        Local,
        Netease,
        Pan,
        Radio
    }

    public class PureLyricInfo
    {
        public string PureLyrics;
        public string TrLyrics;
    }

    public class SongLyric
    {
        public string PureLyric;
        public string Translation;
        public bool HaveTranslation;
        public TimeSpan LyricTime;

        public static SongLyric PureSong = new SongLyric
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "纯音乐 请欣赏" };

        public static SongLyric NoLyric = new SongLyric
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "无歌词 请欣赏" };

        public static SongLyric LoadingLyric = new SongLyric
        { HaveTranslation = false, LyricTime = TimeSpan.Zero, PureLyric = "加载歌词中..." };
    }


    public class NCRadio
    {
        public string name;
        public string id;
        public bool subed;
        public string desc;
        public string cover;
        public NCUser DJ;
        public string lastProgramName;

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

        public static new NCFmItem CreateFromJson(JToken song)
        {
            return new NCFmItem
            {
                Type = HyPlayItemType.Radio,
                sid = song["mainTrackId"].ToString(),
                songname = song["name"].ToString(),
                Artist = new List<NCArtist>
                {
                    new NCArtist
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
        public int Order = 0;
        public int DspOrder => Order + 1;
        public BitmapImage Cover => new BitmapImage(new Uri(Album.cover + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER));
        public bool LoadList = false;
        public bool IsAvailable = true;

        public string ArtistString
        {
            get { return string.Join(" / ", Artist.Select(t => t.name)); }
        }

        public double LengthInMilliseconds;
        public int mvid;
        public string sid;
        public string songname;
        public string transname;
        public HyPlayItemType Type;

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
                Type = HyPlayItemType.Netease,
                Album = NCAlbum.CreateFromJson(song[alpath]),
                sid = song["id"].ToString(),
                songname = song["name"].ToString(),
                Artist = new List<NCArtist>(),
                LengthInMilliseconds = double.Parse(song[dtpath].ToString())
            };
            song[arpath].ToList().ForEach(t => { NCSong.Artist.Add(NCArtist.CreateFromJson(t)); });
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
            return source == null ? "" : "(" + source + ")";
        }
    }

    public class PlayItem
    {
        public bool isLocalFile;
        public StorageFile DontSetLocalStorageFile; //如非特殊原因请不要设置这个东西!
        public int bitrate;
        public string tag;
        public string id;
        public string Name;
        public HyPlayItemType Type;
        public List<NCArtist> Artist;

        public string ArtistString
        {
            get
            {
                return string.Join(" / ", Artist.Select(t => t.name));
            }
        }
        public NCAlbum Album;
        public string AlbumString => Album.name;
        public string url;
        public string subext;
        public string size;
        public double LengthInMilliseconds;

        public NCSong ToNCSong()
        {
            return new NCSong
            {
                Type = Type,
                Album = Album,
                Artist = Artist,
                LengthInMilliseconds = LengthInMilliseconds,
                sid = id,
                songname = Name
            };
        }
    }

    public class NCPlayList
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
                    trackCount = json["trackCount"].ToObject<long>()
                };
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
        public string id;
        public string name;
        public string avatar;
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
        public string id;
        public string title;
        public string description;
        public string cover;
        public int duration;

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
        public HyPlayItemType Type;
        public string id;
        public string name;
        public string avatar;
        public string transname;
        public string alias;

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
        public string id;
        public string name;
        public string cover;
        public string alias;
        public string description;

        public static NCAlbum CreateFromJson(JToken album)
        {
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
        public bool IsByMyself => uid == Common.LoginedUser.id;
        public int ReplyCount;

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
}