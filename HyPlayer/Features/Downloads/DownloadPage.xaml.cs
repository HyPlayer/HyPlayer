using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Downloads.Services;
using WinRT;

namespace HyPlayer.Features.Downloads;

public sealed partial class DownloadPage : Page
{
    public DownloadPageViewModel ViewModel { get; } =
        Ioc.Default.GetRequiredService<DownloadPageViewModel>();

    public DownloadPage()
    {
        InitializeComponent();
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender?.As<Button>()?.DataContext is DownloadObject download)
            ViewModel.ToggleDownloadCommand.Execute(download);
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender?.As<Button>()?.DataContext is DownloadObject download)
            ViewModel.RemoveCommand.Execute(download);
    }
}