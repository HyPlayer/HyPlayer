using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain.Navigation;
using ObservableCollections;

namespace HyPlayer.Shell.Navigation;

public sealed partial class NavigationNode : ObservableObject
{
    public NavigationNode()
    {
        ChildrenView = Children.ToNotifyCollectionChanged();
    }

    [ObservableProperty] public partial string Title { get; set; } = string.Empty;

    [ObservableProperty] public partial IconElement? Icon { get; set; }

    public AppRoute? Route { get; set; }
    public AppNavigationAction? Action { get; set; }
    public bool IsHeader { get; set; }
    public bool IsSeparator { get; set; }
    public bool SelectsOnInvoked { get; set; } = true;

    [ObservableProperty] public partial bool IsVisible { get; set; } = true;

    public ObservableList<NavigationNode> Children { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<NavigationNode> ChildrenView { get; }
}
