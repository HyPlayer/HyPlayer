using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain;

namespace HyPlayer.UI.Controls;

public sealed partial class SingleCommentState : ObservableObject
{
    [ObservableProperty]
    public partial UserDisplay? CommentUserDisplay { get; set; }
}
