using HyPlayer.NeteaseApi.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.Classes
{
    public class Comment
    {
        public Comment thisComment => this; //绑定回去用
        public string CommentId { get; set; }
        public string Content { get; set; }
        public bool HasLiked { get; set; }
        public bool IsMainComment { get; set; } = true;
        public int LikedCount { get; set; }
        public int ReplyCount { get; set; }
        public string ResourceId { get; set; }
        public NeteaseResourceType ResourceType { get; set; }
        public DateTime SendTime { get; set; }
        public NCUser CommentUser { get; set; }
        public bool IsByMyself => CommentUser.Id == Common.LoginedUser?.Id;
    }
}
