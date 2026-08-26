using System;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Platform.Xaml;

namespace HyPlayer.Features.Search;

public sealed partial class Search : Page
{
    public SearchViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<SearchViewModel>();

    public Search()
    {
        InitializeComponent();
        NavigationViewSelector.SelectedItem = NavigationViewSelector.MenuItems[0];
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string keyword || string.IsNullOrWhiteSpace(keyword))
            return;

        if (Convert.ToBase64String(keyword.ToByteArrayUtf8()) == "6Ieq5p2A")
        {
            _ = Launcher.LaunchUriAsync(new Uri("http://music.163.com/m/topic/18926801"));
            return;
        }

        ViewModel.Search(keyword);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SearchResultContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private void NavigationView_OnSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            ViewModel.SelectCategory(tag);
    }

    private void HistoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: { } item })
            ViewModel.Search(item.ToString());
    }
}
