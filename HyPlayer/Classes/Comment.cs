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
        public bool IsMainComment { get; set; }
    }
}
