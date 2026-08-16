using CommunityToolkit.Mvvm.ComponentModel;
using ObservableCollections;

namespace HyPlayer.UI.Lists;

/// <summary>
///     Observable presentation state owned by <see cref="ContainerItemsView" />.
///     The control's dependency properties remain its external XAML contract.
/// </summary>
public sealed partial class ContainerItemsViewState : ObservableObject
{
    public ContainerItemsViewState(ObservableList<ProvidableItemRowViewModel> rows)
    {
        Rows = rows;
        RowsView = Rows.ToNotifyCollectionChanged();
        VisibleRowsView = VisibleRows.ToNotifyCollectionChanged();
        GroupedItemsView = GroupedItems.ToNotifyCollectionChanged();
        RowsView.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsInitialLoading));
            OnPropertyChanged(nameof(IsLoadingMore));
        };
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
    public ObservableList<ProvidableItemRowViewModel> VisibleRows { get; } = [];
    public ObservableList<ProvidableItemRowGroup> GroupedItems { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowViewModel> RowsView { get; }
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowViewModel> VisibleRowsView { get; }
    public NotifyCollectionChangedSynchronizedViewList<ProvidableItemRowGroup> GroupedItemsView { get; }

    public bool IsInitialLoading => IsLoading && Rows.Count == 0;
    public bool IsLoadingMore => IsLoading && Rows.Count > 0;
    public bool CanLoadMore => CanRetry && !IsLoading;
}
