using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TagLib;
using Windows.Networking.BackgroundTransfer;

namespace HyPlayer.Contracts.Services
{
    public interface IDownloadManagementService
    {
        ObservableCollection<DownloadTask> DownloadTasks { get; }
        BackgroundDownloader Downloader { get; }
        List<Task> WritingTask { get; }
        Dictionary<string, Picture> AlbumPictureCache { get; }

        bool CheckDownloadAbilityAndToast();
        void AddDownload(NCSong song);
        void AddDownload(List<NCSong> songs);

    }
}
