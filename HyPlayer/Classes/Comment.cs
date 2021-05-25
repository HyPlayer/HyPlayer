using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public struct Comment
    {
        public NCSong song;
        public string cid;
        public Uri AvatarUri;
        public string Nickname;
        public string content;
        public bool HasLiked;
    }
}
