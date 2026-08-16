using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;
using WinRT;

namespace HyPlayer.UI.Lists;

public sealed partial class CommentsList : UserControl
{
    private NotifyCollectionChangedSynchronizedViewList<CommentBase>? _comments;

    public CommentsList()
    {
        InitializeComponent();
    }

    public NotifyCollectionChangedSynchronizedViewList<CommentBase>? Comments
    {
        get => _comments;
        set
        {
            _comments = value;
            CommentsContainer.ItemsSource = value;
        }
    }

    public ISupportIncrementalLoading? IncrementalSource { get; set; }

    public ScrollViewer CommentPresentScrollViewer =>
        (VisualTreeHelper.GetChild(CommentsContainer, 0)?.As<Border>()).Child?.As<ScrollViewer>();

    private async void IncrementalLoadSentinel_EffectiveViewportChanged(
        FrameworkElement sender,
        EffectiveViewportChangedEventArgs args)
    {
        if (args.BringIntoViewDistanceY > sender.ActualHeight
            || IncrementalSource is not ISupportIncrementalLoading { HasMoreItems: true } incrementalSource)
            return;

        await incrementalSource.LoadMoreItemsAsync(20);
    }
}
