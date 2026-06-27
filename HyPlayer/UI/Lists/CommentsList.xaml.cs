using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using WinRT;

namespace HyPlayer.UI.Lists;

public sealed partial class CommentsList : UserControl
{
    public CommentsList()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty CommentsProperty = DependencyProperty.Register(
        "Comment",
        typeof(ObservableCollection<CommentBase>),
        typeof(CommentsList),
        new PropertyMetadata(null));

    public ObservableCollection<CommentBase> Comments
    {
        get => (ObservableCollection<CommentBase>)GetValue(CommentsProperty);
        set => SetValue(CommentsProperty, value);
    }

    public ScrollViewer CommentPresentScrollViewer
    {
        get => (VisualTreeHelper.GetChild(CommentsContainer, 0)?.As<Border>()).Child?.As<ScrollViewer>();
    }
}
