#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Downloads;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class DownloadPage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IDownloadService _downloadService = Ioc.Default.GetRequiredService<IDownloadService>();

    public DownloadPage()
    {
        InitializeComponent();
    }

    private async void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchFolderPathAsync(_setting.downloadDir);
    }

    private void Button_CleanAll_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.ClearCompleted();
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender?.As<Button>())?.DataContext is not DownloadObject downloadObject) return;
        switch (downloadObject.Status)
        {
            case DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing:
                _downloadService.Pause(downloadObject);
                break;
            case DownloadObject.DownloadStatus.Paused:
                _downloadService.Resume(downloadObject);
                break;
            case DownloadObject.DownloadStatus.Error:
                _downloadService.Retry(downloadObject);
                break;
        }
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender?.As<Button>())?.DataContext is not DownloadObject downloadObject) return;
        _downloadService.Remove(downloadObject);
    }

    private void PauseAllBtn_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.PauseAll();
    }

    private void Resume_All(object sender, RoutedEventArgs e)
    {
        _downloadService.ResumeAll();
    }
}
