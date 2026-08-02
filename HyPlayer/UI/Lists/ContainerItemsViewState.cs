using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.UI.Lists;

/// <summary>
///     Observable presentation state owned by <see cref="ContainerItemsView" />.
///     The control's dependency properties remain its external XAML contract.
/// </summary>
public sealed partial class ContainerItemsViewState : ObservableObject
{
    public ContainerItemsViewState()
    {
        Rows.CollectionChanged += (_, _) =>
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

    [ObservableProperty] public partial bool MultiSelect { get; set; }

    [ObservableProperty] public partial object? ActiveItemsSource { get; set; }

    public ObservableCollection<ProvidableItemRowViewModel> Rows { get; } = [];
    public ObservableCollection<ProvidableItemRowViewModel> VisibleRows { get; } = [];
    public ObservableCollection<ProvidableItemRowGroup> GroupedItems { get; } = [];

    public bool IsInitialLoading => IsLoading && Rows.Count == 0;
    public bool IsLoadingMore => IsLoading && Rows.Count > 0;
    public bool CanLoadMore => HasMore && !IsLoading;
}