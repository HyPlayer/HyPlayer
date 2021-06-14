using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class SingleDownload : UserControl
    {
        int order = 0;
        DownloadObject dobj => DownloadManager.DownloadLists[order];
        public SingleDownload(int ord)
        {
            this.InitializeComponent();
            order = ord;
        }

        public void UpdateUI()
        {
            if (DownloadManager.DownloadLists.Count <= order) return;
            DName.Text = dobj.ncsong.songname;
            DProg.Value = dobj.progress;
            if (dobj.Status == 1)
            {
                DProgText.Text = $"{dobj.HavedSize} / {dobj.TotalSize}";
            }
            else if (dobj.Status == 0)
            {
                DProgText.Text = "排队中";
            }
            else if (dobj.Status == 3)
            {
                DProgText.Text = "暂停中";
            }
        }

        private void ButtonStop_Click(object sender, RoutedEventArgs e)
        {
            dobj.downloadOperation = null;
            dobj.Status = 2;
            DownloadManager.DownloadLists.RemoveAt(order);
        }

        private void ButtonPause_Click(object sender, RoutedEventArgs e)
        {
            if (dobj.Status == 3)
            {
                if (dobj.downloadOperation == null)
                    dobj.StartDownload();
                else
                    dobj.downloadOperation.Resume();
            }
            else
            {
                dobj.downloadOperation?.Pause();
                dobj.Status = 3;
            }

        }
    }
}
