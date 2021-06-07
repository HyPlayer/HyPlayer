using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;
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


    public struct NCSong
    {
        public string sid;
        public string songname;
        public List<NCArtist> Artist;
        public NCAlbum Album;
        public double LengthInMilliseconds;

        public static NCSong CreateFromJson(JToken song)
        {
            string alpath = "album";
            string arpath = "artist";
            if (song[alpath] == null)
                alpath = "al";
            if (song[arpath] == null)
                arpath = "ar";
            NCSong NCSong = new NCSong()
            {
                Album = NCAlbum.CreateFormJson(song[alpath]),
                sid = song["id"].ToString(),
                songname = song["name"].ToString(),
                Artist = new List<NCArtist>(),
                LengthInMilliseconds = double.Parse(song["dt"].ToString())
            };
            song[arpath].ToList().ForEach(t => { NCSong.Artist.Add(NCArtist.CreateFormJson(t)); });
            return NCSong;
        }
    }

    public struct NCPlayItem
    {
        public bool hasLocalFile;
        public StorageFile LocalStorageFile;
        public int bitrate;
        public string tag;
        public string sid;
        public string songname;
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
                sid = sid,
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

        public static NCPlayList CreateFromJson(JToken json)
        {
            try
            {
                return new NCPlayList()
                {
                    cover = json["coverImgUrl"].ToString(),
                    creater = NCUser.CreateFromJson(json["creator"]),
                    desc = json["description"].ToString(),
                    name = json["name"].ToString(),
                    plid = json["id"].ToString(),
                    subscribed = json["subscribed"].ToString()=="True"
                };
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
                return new NCUser()
                {
                    avatar = user["avatarUrl"].ToString(),
                    id = user["userId"].ToString(),
                    name = user["nickname"].ToString(),
                    signature = user["signature"].ToString()
                };
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
        public string id;
        public string name;
        public string cover;
        public string alias;
        public string description;

        public static NCAlbum CreateFormJson(JToken album)
        {
            return new NCAlbum()
            {
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
}