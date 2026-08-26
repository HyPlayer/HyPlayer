using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Platform.Runtime.Background;

namespace HyPlayer.Features.Radio;

public sealed partial class RadioPage : Page
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public RadioPageViewModel ViewModel { get; } =
        Ioc.Default.GetRequiredService<RadioPageViewModel>();

    public RadioPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _taskRunner.Forget(
            ViewModel.LoadAsync(e.Parameter, _cancellationTokenSource.Token),
            "load radio page");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        SongContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        await SongContainer.PlayAllAsync();
    }
}
