#region

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.User;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Lists.IncrementalLoading;
using ObservableCollections;
using Microsoft.UI.Xaml.Controls;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了"用户控件"项模板

namespace HyPlayer.UI.Controls;

public sealed partial class SingleComment : UserControl
{
    public static readonly DependencyProperty AvatarSourceProperty =
        DependencyProperty.Register("AvatarSource", typeof(BitmapImage), typeof(SingleComment),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MainCommentProperty =
        DependencyProperty.Register("MainComment", typeof(CommentBase), typeof(SingleComment),
            new PropertyMetadata(null)); //主评论

    private readonly UISettings _setting = Ioc.Default.GetRequiredService<UISettings>();

    private readonly SwitchingIncrementalSource<CommentBase> _floorCommentSource = new();
    private readonly IncrementalLoadingCollection<SwitchingIncrementalSource<CommentBase>, CommentBase> _floorComments;
    private readonly NotifyCollectionChangedSynchronizedViewList<CommentBase> _floorCommentsView;

    public SingleComment()
    {
        _floorComments = new IncrementalLoadingCollection<SwitchingIncrementalSource<CommentBase>, CommentBase>(
            _floorCommentSource,
            itemsPerPage: 20,
            onError: FloorComments_LoadFailed);
        _floorCommentsView = _floorComments.ToNotifyCollectionChanged();
        InitializeComponent();
    }

    public SingleCommentState State { get; } = new();

    public BitmapImage AvatarSource
    {
        get => (BitmapImage)GetValue(AvatarSourceProperty);
        set => SetValue(AvatarSourceProperty, value);
    }

    public CommentBase MainComment
    {
        get => (CommentBase)GetValue(MainCommentProperty);
        set
        {
            SetValue(MainCommentProperty, value);
            ReplyCountIndicator.Text = value.ReplyCount.ToString();
            LikeCountTB.Text = value.LikedCount.ToString();
        }
    }

    private async Task ResetAndLoadFloorCommentsAsync()
    {
        if (!TryResolveCommentTarget(MainComment, out var itemId, out var typeId))
        {
            _floorCommentSource.Reset(null);
            _floorComments.Clear();
            return;
        }

        _floorCommentSource.Reset(new ThreadedCommentPageSource(
            Ioc.Default.GetRequiredService<ICommentProvidable>(),
            MainComment,
            itemId,
            typeId));
        LoadMore.Visibility = Visibility.Collapsed;
        _floorComments.Clear();
        await _floorComments.RefreshAsync();
    }

    private void FloorComments_LoadFailed(Exception exception)
    {
        LoadMore.Visibility = Visibility.Visible;
        Ioc.Default.GetRequiredService<INotificationService>()
            .ShowMessage("加载回复失败", exception.Message);
    }

    private async void Like_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryResolveCommentTarget(MainComment, out var itemId, out var typeId))
                return;

            await Ioc.Default.GetRequiredService<ICommentProvidable>().SetCommentLikeStateAsync(
                itemId,
                typeId,
                MainComment.ActualId,
                !MainComment.HasLiked);
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("点赞失败", ex.Message);
            return;
        }

        MainComment.LikedCount += MainComment.HasLiked ? -1 : 1;
        MainComment.HasLiked = !MainComment.HasLiked;
        LikeCountTB.Text = MainComment.LikedCount.ToString();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
        // NOTE: Comment delete functionality not yet implemented
    }

    private void NavToUser_Click(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<INavigationService>().Navigate(typeof(Me), MainComment.Sender?.ActualId);
    }

    private async void SendReply_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReplyText.Text) && Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn)
        {
            try
            {
                // NOTE: Comment send functionality not yet implemented
                ReplyText.Text = string.Empty;
                await Task.Delay(1000);
                _ = ResetAndLoadFloorCommentsAsync();
            }
            catch (Exception ex)
            {
                var dlg = new MessageDialog(ex.Message, "出现问题，评论失败");
                await dlg.ShowAsync();
            }
        }
        else if (string.IsNullOrWhiteSpace(ReplyText.Text))
        {
            var dlg = new MessageDialog("评论不能为空");
            await dlg.ShowAsync();
        }
        else
        {
            var dlg = new MessageDialog("请先登录");
            await dlg.ShowAsync();
        }
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        _ = ResetAndLoadFloorCommentsAsync();
    }


    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        State.CommentUserDisplay = new UserDisplay(
            new CommentUserInfo
            {
                ActualId = MainComment.Sender?.ActualId ?? string.Empty,
                Name = MainComment.Sender?.Name ?? string.Empty,
                AvatarUrl = await GetCommentAvatarUrlAsync(MainComment)
            },
            _setting.NoImage);
        ReplyBtn.Visibility = Visibility.Visible;
        FloorCommentsExpander.Visibility = MainComment.IsMainComment ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FloorCommentsExpander_Expanding(Expander sender,
        ExpanderExpandingEventArgs args)
    {
        _ = ResetAndLoadFloorCommentsAsync();
    }

    private void FloorCommentsExpander_Collapsed(Expander sender,
        ExpanderCollapsedEventArgs args)
    {
        _floorCommentSource.Reset(null);
        _floorComments.Clear();
    }

    private static async Task<string?> GetCommentAvatarUrlAsync(CommentBase comment)
    {
        if (comment.Sender is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        if (result is not IResourceResultOf<Uri?> uriResult || result.ResourceStatus != ResourceStatus.Success)
            return null;

        return (await uriResult.GetResourceAsync())?.ToString();
    }

    private static bool TryResolveCommentTarget(CommentBase comment, out string itemId, out string typeId)
    {
        itemId = string.Empty;
        typeId = string.Empty;
        if (string.IsNullOrWhiteSpace(comment.ProvidableItemId))
            return false;

        var knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
        var fullId = comment.ProvidableItemId;
        var providerScopedId = fullId.StartsWith(knownTypeIds.Id, StringComparison.Ordinal)
            ? fullId[knownTypeIds.Id.Length..]
            : fullId;
        if (providerScopedId.Length < 2)
            return false;

        typeId = providerScopedId[..2];
        itemId = providerScopedId[2..];
        return !string.IsNullOrWhiteSpace(itemId);
    }

    private sealed class ThreadedCommentPageSource(
        ICommentProvidable commentProvider,
        CommentBase mainComment,
        string itemId,
        string typeId) : IIncrementalSource<CommentBase>
    {
        private int _offset;

        private bool _hasMore = true;

        public async Task<IEnumerable<CommentBase>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (!_hasMore)
                return [];

            var count = Math.Clamp(pageSize, 1, 20);
            var result = await SimpleCacher.GetOrCreateCacheAsync(
                CacheType.Comments,
                $"{mainComment.ProvidableItemId}_{mainComment.ActualId}_{_offset}_{count}",
                () => commentProvider.GetThreadedCommentsAsync(
                    itemId,
                    typeId,
                    mainComment.ActualId,
                    _offset,
                    count,
                    cancellationToken),
                TimeSpan.FromMinutes(5));

            cancellationToken.ThrowIfCancellationRequested();
            var items = result?.Items ?? [];
            foreach (var comment in items)
            {
                comment.ProvidableItemId = mainComment.ProvidableItemId;
                comment.IsMainComment = false;
            }

            _offset = result?.NextOffset ?? _offset + items.Count;
            _hasMore = result?.HasMore is true && items.Count > 0;
            return items;
        }
    }
}
