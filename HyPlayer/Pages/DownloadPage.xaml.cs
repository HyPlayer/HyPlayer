#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.HyPlayControl;
using HyPlayer.ViewModels;
using System;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class DownloadPage : Page
{
    private DownloadViewModel ViewModel => (DownloadViewModel)DataContext;

    public DownloadPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<DownloadViewModel>();
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender?.As<Button>())?.DataContext is not DownloadObject downloadObject) return;
        switch (downloadObject.Status)
        {
            case DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing:
                downloadObject.Pause();
                break;
            case DownloadObject.DownloadStatus.Paused:
                downloadObject.Resume();
                break;
            case DownloadObject.DownloadStatus.Error:
                downloadObject.Message = "等待中";
                downloadObject.Progress = 0;
                downloadObject.Status = DownloadObject.DownloadStatus.Queueing;
                break;
        }
    }

    private void RemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender?.As<Button>())?.DataContext is not DownloadObject downloadObject) return;
        downloadObject.Remove();
    }
}
