using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using WinRT;

namespace HyPlayer.UI.Lists;

public sealed partial class CommentsList : UserControl
{
    public static readonly DependencyProperty CommentsProperty = DependencyProperty.Register(
        nameof(Comments),
        typeof(ObservableCollection<CommentBase>),
        typeof(CommentsList),
        new PropertyMetadata(null));

    public CommentsList()
    {
        InitializeComponent();
    }

    public ObservableCollection<CommentBase> Comments
    {
        get => (ObservableCollection<CommentBase>)GetValue(CommentsProperty);
        set => SetValue(CommentsProperty, value);
    }

    public ScrollViewer CommentPresentScrollViewer =>
        (VisualTreeHelper.GetChild(CommentsContainer, 0)?.As<Border>()).Child?.As<ScrollViewer>();

    private async void IncrementalLoadSentinel_EffectiveViewportChanged(
        FrameworkElement sender,
        EffectiveViewportChangedEventArgs args)
    {
        if (args.BringIntoViewDistanceY > sender.ActualHeight
            || Comments is not ISupportIncrementalLoading { HasMoreItems: true } incrementalSource)
            return;

        await incrementalSource.LoadMoreItemsAsync(20);
    }
}
