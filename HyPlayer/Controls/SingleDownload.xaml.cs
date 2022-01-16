#region

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using HyPlayer.HyPlayControl;

#endregion

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls;

public sealed partial class SingleDownload : UserControl
{
    private readonly int order;

    public SingleDownload(int ord)
    {
        InitializeComponent();
        order = ord;
    }

    private DownloadObject dobj => DownloadManager.DownloadLists[order];

    public static string GetSize(double size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double mod = 1024.0;
        int i = 0;
        while (size >= mod)
        {
            size /= mod;
            i++;
        }

        return Math.Round(size, 2) + units[i];
    }

    public void UpdateUI()
    {
        if (DownloadManager.DownloadLists.Count <= order) return;
        DName.Text = dobj.ncsong.ArtistString + " - " + dobj.ncsong.songname;
        DProg.Value = dobj.progress;
        if (dobj.Status == 1)
            DProgText.Text = $"{GetSize(dobj.HavedSize)} / {GetSize(dobj.TotalSize)}";
        else if (dobj.Status == 0)
            DProgText.Text = "排队中";
        else if (dobj.Status == 3) DProgText.Text = "暂停中";
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