using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Comments;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using ObservableCollections;

namespace HyPlayer.Features.Comments;

public partial class CommentsViewModel : ObservableObject
{
    private readonly IProvidableItemCommentProvidable _commentProvider;
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly INotificationService _notification;
    private readonly ObservableList<CommentBase> _comments = [];
    private int _commentsLoadVersion;
    private string? _cursor;
    private CommentTarget? _target;

    public CommentsViewModel(
        IProvidableItemCommentProvidable commentProvider,
        IProviderKnownTypeIds knownTypeIds,
        INotificationService notification)
    {
        _commentProvider = commentProvider;
        _knownTypeIds = knownTypeIds;
        _notification = notification;
        Comments = _comments.ToNotifyCollectionChanged();
    }

    public NotifyCollectionChangedSynchronizedViewList<CommentBase> Comments { get; }

    [ObservableProperty] public partial bool HasNextPage { get; set; }
    [ObservableProperty] public partial bool HasPreviousPage { get; set; }
    [ObservableProperty] public partial int Page { get; set; } = 1;
    [ObservableProperty] public partial int SortType { get; set; } = 3;

    public async Task LoadAsync(CommentTarget target, CancellationToken cancellationToken)
    {
        _target = target;
        Page = 1;
        _cursor = null;
        await LoadCurrentPageAsync(cancellationToken);
    }

    public Task NextPageAsync(CancellationToken cancellationToken)
    {
        if (!HasNextPage)
            return Task.CompletedTask;
        Page++;
        return LoadCurrentPageAsync(cancellationToken);
    }

    public Task PreviousPageAsync(CancellationToken cancellationToken)
    {
        if (Page <= 1)
            return Task.CompletedTask;
        Page--;
        return LoadCurrentPageAsync(cancellationToken);
    }

    public Task GoToPageAsync(int page, CancellationToken cancellationToken)
    {
        if (page < 1)
            return Task.CompletedTask;
        Page = page;
        return LoadCurrentPageAsync(cancellationToken);
    }

    public Task ChangeSortTypeAsync(int sortType, CancellationToken cancellationToken)
    {
        if (sortType is < 1 or > 3)
            return Task.CompletedTask;
        SortType = sortType;
        Page = 1;
        _cursor = null;
        return LoadCurrentPageAsync(cancellationToken);
    }

    private async Task LoadCurrentPageAsync(CancellationToken cancellationToken)
    {
        if (_target is null)
            return;

        var version = ++_commentsLoadVersion;
        var offset = SortType == 3 && Page != 1 && int.TryParse(_cursor, out var cursorOffset)
            ? cursorOffset
            : (Page - 1) * 20;
        var result = await LoadProviderCommentsAsync(offset, SortType, cancellationToken);
        if (version != _commentsLoadVersion)
            return;

        foreach (var comment in result.Items)
            comment.ProvidableItemId = _knownTypeIds.Id + _target.TypeId + _target.ResourceId;

        _comments.Clear();
        _comments.AddRange(result.Items);
        if (SortType == 3)
            _cursor = result.NextOffset?.ToString();
        HasNextPage = result.HasMore;
        HasPreviousPage = Page > 1;
    }

    private async Task<ProviderPageResult<CommentBase>> LoadProviderCommentsAsync(
        int offset,
        int type,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _commentProvider.GetCommentsAsync(
                _target!.ResourceId,
                _target.TypeId,
                offset,
                20,
                type,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("加载评论时出错", ex.Message);
            return new ProviderPageResult<CommentBase> { Items = [], HasMore = false };
        }
    }
}
