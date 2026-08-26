using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.System;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using ObservableCollections;

namespace HyPlayer.Features.Downloads;

public partial class DownloadPageViewModel
{
    private readonly IDownloadService _downloadService;
    private readonly DownloadSettings _settings;

    public DownloadPageViewModel(IDownloadService downloadService, DownloadSettings settings)
    {
        _downloadService = downloadService;
        _settings = settings;
        Downloads = downloadService.Downloads.ToNotifyCollectionChanged();
    }

    public NotifyCollectionChangedSynchronizedViewList<DownloadObject> Downloads { get; }

    [RelayCommand]
    private async Task OpenDownloadFolderAsync()
    {
        await Launcher.LaunchFolderPathAsync(_settings.DownloadDirectory);
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        _downloadService.ClearCompleted();
    }

    [RelayCommand]
    private void ToggleDownload(DownloadObject download)
    {
        switch (download.Status)
        {
            case DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing:
                _downloadService.Pause(download);
                break;
            case DownloadObject.DownloadStatus.Paused:
                _downloadService.Resume(download);
                break;
            case DownloadObject.DownloadStatus.Error:
                _downloadService.Retry(download);
                break;
        }
    }

    [RelayCommand]
    private void Remove(DownloadObject download)
    {
        _downloadService.Remove(download);
    }

    [RelayCommand]
    private void PauseAll()
    {
        _downloadService.PauseAll();
    }

    [RelayCommand]
    private void ResumeAll()
    {
        _downloadService.ResumeAll();
    }
}
