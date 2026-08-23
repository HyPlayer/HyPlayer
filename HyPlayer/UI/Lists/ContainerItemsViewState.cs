using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using ObservableCollections;

namespace HyPlayer.UI.Lists;

/// <summary>
///     Observable presentation state owned by <see cref="ContainerItemsView" />.
///     The control's dependency properties remain its external XAML contract.
/// </summary>
public sealed partial class ContainerItemsViewState : ObservableObject, IDisposable
{
    private bool _isDisposed;
    private readonly ISynchronizedView<ProvidableItemRowViewModel, ProvidableItemRowViewModel> _allRowsViewSource;
    private readonly ISynchronizedView<ProvidableItemRowViewModel, ProvidableItemRowViewModel> _visibleRowsViewSource;
    private readonly ISynchronizedView<ProvidableItemRowGroup, ProvidableItemRowGroup> _groupedItemsViewSource;

    public ContainerItemsViewState(ObservableList<ProvidableItemRowViewModel> rows)
    {
        Rows = rows;
        _allRowsViewSource = Rows.CreateView(static row => row);
        _visibleRowsViewSource = Rows.CreateView(static row => row);
        RowsView = _allRowsViewSource.ToNotifyCollectionChanged();
        VisibleRowsView = _visibleRowsViewSource.ToNotifyCollectionChanged();
        _groupedItemsViewSource = GroupedItems.CreateView(static group => group);
        GroupedItemsView = _groupedItemsViewSource.ToNotifyCollectionChanged();
        RowsView.CollectionChanged += RowsView_CollectionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInitialLoading))]
    [NotifyPropertyChangedFor(nameof(IsLoadingMore))]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool CanRetry { get; set; }

    [ObservableProperty] public partial bool MultiSelect { get; set; }

    [ObservableProperty] public partial object? ActiveItemsSource { get; set; }

    public ObservableList<ProvidableItemRowViewModel> Rows { get; }
    public ObservableList<ProvidableItemRowGroup> GroupedItems { get; } = [];
    public IReadOnlyCollection<ProvidableItemRowViewModel> VisibleRows => _visibleRowsViewSource;
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowViewModel> RowsView { get; }
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowViewModel> VisibleRowsView { get; }
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowGroup> GroupedItemsView { get; }

    public bool IsInitialLoading => IsLoading && Rows.Count == 0;
    public bool IsLoadingMore => IsLoading && Rows.Count > 0;
    public bool CanLoadMore => CanRetry && !IsLoading;

    public void ApplyFilter(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            _visibleRowsViewSource.ResetFilter();
        else
            _visibleRowsViewSource.AttachFilter(row => row.MatchesFilter(filterText));
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
        RowsView.Dispose();
        VisibleRowsView.Dispose();
        GroupedItemsView.Dispose();
        _allRowsViewSource.Dispose();
        _visibleRowsViewSource.Dispose();
        _groupedItemsViewSource.Dispose();
    }

    private void RowsView_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(IsLoadingMore));
    }
}
