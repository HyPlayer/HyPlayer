using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.System;

namespace HyPlayer.ViewModels
{
    public partial class DownloadViewModel : ObservableRecipient
    {
        public DownloadViewModel() 
        {
        }

        [RelayCommand]
        private async void OpenDownloadFolder()
        {
            await Launcher.LaunchFolderPathAsync(Common.Setting.downloadDir);
        }

        [RelayCommand]
        private void CleanAll()
        {
            DownloadManager.DownloadLists.Clear();
        }

        [RelayCommand]
        private void PauseAll()
        {
            foreach (var downloadObject in DownloadManager.DownloadLists.Where(t =>
                     t.Status is DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing))
            {
                downloadObject.Pause();
            }
        }

        [RelayCommand]
        private void ResumeAll()
        {
            foreach (var downloadObject in DownloadManager.DownloadLists.Where(t =>
                     t.Status != DownloadObject.DownloadStatus.Downloading))
            {
                if (downloadObject.Status == DownloadObject.DownloadStatus.Paused)
                {
                    downloadObject.Message = "排队中";
                    downloadObject.HasPaused = false;
                }

                if (downloadObject.Status == DownloadObject.DownloadStatus.Error)
                {
                    downloadObject.Message = "排队中";
                    downloadObject.Progress = 0;
                    downloadObject.HasPaused = false;
                    downloadObject.HasError = false;
                }
                downloadObject.Status = DownloadObject.DownloadStatus.Queueing;
            }
        }
    }
}
