using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Loading;
using ObservableCollections;

namespace HyPlayer.UI.Lists;

/// <summary>
///     Observable presentation state owned by <see cref="ContainerItemsView" />.
///     The control's dependency properties remain its external XAML contract.
/// </summary>
public sealed partial class ContainerItemsViewState : ObservableObject, IDisposable
{
    private bool _isDisposed;
    private readonly ISynchronizedView<ProvidableItemRowViewModel, ProvidableItemRowViewModel> _rowsViewSource;
    private readonly ISynchronizedView<ProvidableItemRowGroup, ProvidableItemRowGroup> _groupedItemsViewSource;

    public ContainerItemsViewState(
        ObservableList<ProvidableItemRowViewModel> rows,
        ILoadEnvironment? environment = null)
    {
        Rows = rows;
        Load = new LoadState(environment: environment);
        MoreLoad = new LoadState("更多内容", environment);
        MoreLoad.PropertyChanged += MoreLoad_PropertyChanged;
        _rowsViewSource = Rows.CreateView(static row => row);
        RowsView = _rowsViewSource.ToNotifyCollectionChanged();
        _groupedItemsViewSource = GroupedItems.CreateView(static group => group);
        GroupedItemsView = _groupedItemsViewSource.ToNotifyCollectionChanged();
        RowsView.CollectionChanged += RowsView_CollectionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingMore))]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    [NotifyPropertyChangedFor(nameof(HasLoadMoreFailure))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool CanRetry { get; set; }

    [ObservableProperty] public partial bool MultiSelect { get; set; }

    [ObservableProperty] public partial object? ActiveItemsSource { get; set; }

    [ObservableProperty] public partial LoadState? ActiveLoad { get; set; }

    public LoadState Load { get; }
    public LoadState MoreLoad { get; }
    public ObservableList<ProvidableItemRowViewModel> Rows { get; }
    public ObservableList<ProvidableItemRowGroup> GroupedItems { get; } = [];
    public IReadOnlyCollection<ProvidableItemRowViewModel> VisibleRows => _rowsViewSource;
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowViewModel> RowsView { get; }
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowGroup> GroupedItemsView { get; }

    public bool IsLoadingMore => IsLoading && Rows.Count > 0;
    public string LoadingMoreText => MoreLoad.IsSlow
        ? $"已加载 {Rows.Count} 项，网络较慢，仍在加载…"
        : $"已加载 {Rows.Count} 项，正在继续加载…";
    public bool HasLoadMoreFailure => MoreLoad.IsFailed && !IsLoading;
    public string LoadMoreFailureText => MoreLoad.Failure is { } failure ? $"加载更多失败：{failure.Reason}" : string.Empty;
    public bool CanLoadMore => CanRetry && !IsLoading && Rows.Count > 0;

    public void ApplyFilter(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            _rowsViewSource.ResetFilter();
        else
            _rowsViewSource.AttachFilter(row => row.MatchesFilter(filterText));
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        if (!disposing)
            return;

        RowsView.CollectionChanged -= RowsView_CollectionChanged;
        MoreLoad.PropertyChanged -= MoreLoad_PropertyChanged;
        Load.Cancel();
        MoreLoad.Cancel();
        RowsView.Dispose();
        GroupedItemsView.Dispose();
        _rowsViewSource.Dispose();
        _groupedItemsViewSource.Dispose();
    }

    private void RowsView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsLoadingMore));
        OnPropertyChanged(nameof(LoadingMoreText));
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private void MoreLoad_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LoadingMoreText));
        OnPropertyChanged(nameof(HasLoadMoreFailure));
        OnPropertyChanged(nameof(LoadMoreFailureText));
    }
}
