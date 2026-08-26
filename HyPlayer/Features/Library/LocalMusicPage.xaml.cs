using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Features.Downloads;
using HyPlayer.Features.Netease.Legacy;
using HyPlayer.Platform.Runtime.Background;
using WinRT;

namespace HyPlayer.Features.Library;

public sealed partial class LocalMusicPage : Page
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public LocalMusicPageViewModel ViewModel { get; } =
        Ioc.Default.GetRequiredService<LocalMusicPageViewModel>();

    public LocalMusicPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadPageFrame.Navigate(typeof(DownloadPage));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        Bindings.StopTracking();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(
            ViewModel.ScanAsync(_cancellationTokenSource.Token),
            "scan local music");
    }

    private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxLocalMusicContainer.SelectedItem is { } selected)
            ViewModel.PlayCommand.Execute(selected);
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        if (sender?.As<Button>()?.Tag is not string path)
            return;
        var file = await StorageFile.GetFileFromPathAsync(path);
        await CloudUpload.UploadMusic(file);
    }

    private void Add_Local(object sender, RoutedEventArgs e)
    {
        ViewModel.AppendFilesCommand.Execute(null);
    }
}
