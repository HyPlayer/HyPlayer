using System.Threading;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Platform.Runtime.Background;

namespace HyPlayer.Features.Library;

public sealed partial class HistoryPage : Page
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public HistoryViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<HistoryViewModel>();

    public HistoryPage()
    {
        InitializeComponent();
        HisModeNavView.SelectedItem = SongHis;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        ItemsList.ReleaseResources();
        Bindings.StopTracking();
    }

    private void NavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag } ||
            !System.Enum.TryParse<HistoryMode>(tag, out var mode))
            return;

        _taskRunner.Forget(
            ViewModel.SelectModeAsync(mode, _cancellationTokenSource.Token),
            "load history mode");
    }
}
