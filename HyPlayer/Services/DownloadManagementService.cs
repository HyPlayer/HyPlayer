using HyPlayer.Classes;
using HyPlayer.Contracts.Services;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Timers;
using TagLib;
using Windows.Networking.BackgroundTransfer;

namespace HyPlayer.Services
{
    public class DownloadManagementService : IDownloadManagementService
    {
        private readonly System.Timers.Timer _timer = new(1000);
        private bool Timered;

        public ObservableCollection<DownloadTask> DownloadTasks => new ObservableCollection<DownloadTask>();
        public BackgroundDownloader Downloader => new BackgroundDownloader();
        public List<Task> WritingTask => new List<Task>();
        public Dictionary<string, Picture> AlbumPictureCache => new Dictionary<string, Picture>();

        public void AddDownload(NCSong song)
        {
            if (!CheckDownloadAbilityAndToast()) return;
            if (!Timered)
            {
                _timer.Elapsed += Timer_Elapsed;
                _timer.Start();
                Timered = true;
            }

            DownloadTasks.Add(new DownloadTask(song));
        }

        public void AddDownload(List<NCSong> songs)
        {
            if (!CheckDownloadAbilityAndToast()) return;
            if (!Timered)
            {
                _timer.Elapsed += Timer_Elapsed;
                _timer.Start();
                Timered = true;
            }

            songs.ForEach(t => { DownloadTasks.Add(new DownloadTask(t)); });
        }

        public bool CheckDownloadAbilityAndToast()
        {
            Common.AddToTeachingTipLists("开始下载");
            return true;
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (DownloadTasks.Count == 0) return;
            var maxDownloadCount = Common.Setting.maxDownloadCount;
            for (var i = 0; i < DownloadTasks.Count; i++)
            {
                switch (DownloadTasks[i].Status)
                {
                    case DownloadStatus.Downloading:
                        if (--maxDownloadCount <= 0) return;
                        continue;
                    case DownloadStatus.Queueing:
                        _ = DownloadTasks[i].StartDownload();
                        --maxDownloadCount;
                        return;
                    case DownloadStatus.Finished:
                        var i1 = i;
                        _ = Common.Invoke(() => { DownloadTasks.RemoveAt(i1); });
                        break;
                    case DownloadStatus.Paused:
                    case DownloadStatus.Error:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
