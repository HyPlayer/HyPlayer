#region

using System;
using System.Collections.Specialized;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Downloads;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class DownloadPage : Page
{
    private readonly IDownloadService _downloadService = Ioc.Default.GetRequiredService<IDownloadService>();
    private readonly DownloadSettings _setting = Ioc.Default.GetRequiredService<DownloadSettings>();
    private readonly LoadState _load = new("下载任务") { EmptyText = "还没有下载任务" };

    public DownloadPage()
    {
        InitializeComponent();
        EmptyState.State = _load;
        Loaded += DownloadPage_Loaded;
        Unloaded += DownloadPage_Unloaded;
    }

    private void DownloadPage_Loaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)DownloadManager.DownloadListsView).CollectionChanged += Downloads_CollectionChanged;
        RefreshEmptyState();
    }

    private void DownloadPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ((INotifyCollectionChanged)DownloadManager.DownloadListsView).CollectionChanged -= Downloads_CollectionChanged;
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.HasThreadAccess)
            RefreshEmptyState();
        else
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshEmptyState);
    }

    private void RefreshEmptyState()
    {
        var isEmpty = DownloadManager.DownloadLists.Count == 0;
        if (isEmpty == _load.IsEmpty && (isEmpty || _load.IsLoaded))
            return;

        if (isEmpty)
            _load.ShowEmpty();
        else
            _load.ShowLoaded();
    }

    private async void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchFolderPathAsync(_setting.DownloadDirectory);
    }

    private void Button_CleanAll_Click(object sender, RoutedEventArgs e)
    {
        _downloadService.ClearCompleted();
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender?.As<Button>()?.DataContext is not DownloadObject downloadObject) return;
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
        if (sender?.As<Button>()?.DataContext is not DownloadObject downloadObject) return;
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