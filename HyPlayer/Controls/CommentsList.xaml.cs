using HyPlayer.Classes;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class CommentsList : UserControl
    {
        public CommentsList()
        {
            this.InitializeComponent();
        }
        public static readonly DependencyProperty CommentsProperty = DependencyProperty.Register
            (
            "Comment", typeof(ObservableCollection<Comment>),
            typeof(CommentsList),
            new PropertyMetadata(null)
        );
        public ObservableCollection<Comment> Comments//列表下评论
        {
            get => (ObservableCollection<Comment>)GetValue(CommentsProperty);
            set
            {
                SetValue(CommentsProperty, value);
            }
        }
        public ScrollViewer CommentPresentScrollViewer

        {
            get => (VisualTreeHelper.GetChild(CommentsContainer, 0) as Border).Child as ScrollViewer;
        }
    }
}
