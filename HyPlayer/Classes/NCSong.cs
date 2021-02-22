using System;
using System.Collections.Generic;

namespace HyPlayer.Classes
{

    public struct NCCookie
    {
        public string name;
        public string value;
        public string path;
        public string domain;
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
        public string name;
    }

    public struct NCAlbum
    {
        public string id;
        public string name;
        public string cover;
    }
}