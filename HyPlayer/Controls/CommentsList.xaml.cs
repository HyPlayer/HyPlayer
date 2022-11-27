using HyPlayer.Classes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

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
