using System;
using System.Collections.Generic;

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
    }

    public struct NCPlayItem
    {
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
    }

    public struct NCPlayList
    {
        public string plid;
        public string cover;
        public string name;
        public string desc;
        public NCUser creater;
    }

    public struct NCUser
    {
        public string id;
        public string name;
        public string avatar;
        public string signature;
    }

    public struct NCArtist
    {
        public string id;
        public object idobj => (object)id;
        public string name;
        public string avatar;
        public Uri avatarUri => new Uri(this.avatar);
    }

    public struct NCAlbum
    {
        public string id;
        public string name;
        public string cover;
    }
}