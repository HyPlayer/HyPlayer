#region

using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;

#endregion

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Features.Library;

/// <summary>
///     An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class PageFavorite : Page
{
    public FavoriteViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<FavoriteViewModel>();

    public PageFavorite()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            ViewModel.SelectCategory(tag);
    }
}