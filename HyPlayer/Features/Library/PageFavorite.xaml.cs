#region

using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using WinRT;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Features.Library;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PageFavorite : Page
{
    public PageFavorite()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<FavoriteViewModel>();
    }

    private FavoriteViewModel ViewModel => (FavoriteViewModel)DataContext;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var item = args.SelectedItem.As<NavigationViewItem>();
        ViewModel.OnSelectionChanged(item);
    }
}