using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
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
    private bool _isLoadingMore;

    public CommentsList()
    {
        InitializeComponent();
    }

    public NotifyCollectionChangedSynchronizedViewList<CommentBase>? Comments
    {
        get => _comments;
        set
        {
            if (ReferenceEquals(_comments, value)) return;
            _comments = value;
            CommentsContainer.ItemsSource = value;
        }
    }

    public ISupportIncrementalLoading? IncrementalSource { get; set; }

    public ScrollViewer CommentPresentScrollViewer =>
        (VisualTreeHelper.GetChild(CommentsContainer, 0)?.As<Border>()).Child?.As<ScrollViewer>();

    private void CommentsContainer_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
            return;

        AutomationProperties.SetName(args.ItemContainer, CommentAccessibleName.Format(args.Item as CommentBase));
    }

    private async void IncrementalLoadSentinel_EffectiveViewportChanged(
        FrameworkElement sender,
        EffectiveViewportChangedEventArgs args)
    {
        if (_isLoadingMore || args.BringIntoViewDistanceY > sender.ActualHeight
            || IncrementalSource is not ISupportIncrementalLoading { HasMoreItems: true } incrementalSource)
            return;

        _isLoadingMore = true;
        try
        {
            await incrementalSource.LoadMoreItemsAsync(20);
        }
        finally
        {
            _isLoadingMore = false;
        }
    }
}
