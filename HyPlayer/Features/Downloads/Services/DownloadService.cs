using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.Downloads.Services;

public sealed class DownloadService : IDownloadService
{
    public ObservableCollection<DownloadObject> Downloads => DownloadManager.DownloadLists;

    public Task AddAsync(SingleSongBase song)
    {
        DownloadManager.AddDownload(song);
        return Task.CompletedTask;
    }

    public Task AddAsync(IEnumerable<SingleSongBase> songs)
    {
        DownloadManager.AddDownload(songs.ToList());
        return Task.CompletedTask;
    }

    public void Pause(DownloadObject download)
    {
        download.Pause();
    }

    public void Resume(DownloadObject download)
    {
        download.Resume();
    }

    public void Retry(DownloadObject download)
    {
        download.Retry();
    }

    public void Remove(DownloadObject download)
    {
        download.Remove();
        Downloads.Remove(download);
    }

    public void ClearCompleted()
    {
        for (var i = Downloads.Count - 1; i >= 0; i--)
        {
            if (Downloads[i].Status is DownloadObject.DownloadStatus.Finished)
                Downloads.RemoveAt(i);
        }
    }

    public void PauseAll()
    {
        foreach (var download in Downloads
                     .Where(item => item.Status is DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing)
                     .ToList())
        {
            download.Pause();
        }
    }

    public void ResumeAll()
    {
        foreach (var download in Downloads
                     .Where(item => item.Status != DownloadObject.DownloadStatus.Downloading)
                     .ToList())
        {
            if (download.Status == DownloadObject.DownloadStatus.Paused)
                download.Queue();
            else if (download.Status == DownloadObject.DownloadStatus.Error)
                download.Retry();
        }
    }
}
