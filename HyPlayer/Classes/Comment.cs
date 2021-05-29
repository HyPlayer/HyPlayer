using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    public struct Comment
    {
        public NCSong song;
        public string cid;
        public string uid;
        public Uri AvatarUri;
        public string Nickname;
        public string content;
        public bool HasLiked;
        public bool IsByMyself => this.uid == Common.LoginedUser.id;
    }
}
