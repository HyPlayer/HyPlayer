using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseApi.Models;
using HyPlayer.Services.Abstractions;
using System;

namespace HyPlayer.Domain.Comments
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
        public bool IsByMyself => CommentUser.Id == Ioc.Default.GetRequiredService<IAuthService>().CurrentUser?.Id;
    }
}
