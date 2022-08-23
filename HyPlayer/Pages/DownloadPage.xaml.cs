#region

using System;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.HyPlayControl;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class DownloadPage : Page
{
    public DownloadPage()
    {
        InitializeComponent();
    }

    private async void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchFolderPathAsync(Common.Setting.downloadDir);
    }

    private void Button_CleanAll_Click(object sender, RoutedEventArgs e)
    {
        DownloadManager.DownloadLists.Clear();
    }

    private void PauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not DownloadObject downloadObject) return;
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
        if ((sender as Button)?.DataContext is not DownloadObject downloadObject) return;
        downloadObject.Remove();
    }

    private void PauseAllBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var downloadObject in DownloadManager.DownloadLists.Where(t =>
                     t.Status is DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Queueing))
        {
            downloadObject.Pause();
        }
    }

    private void Resume_All(object sender, RoutedEventArgs e)
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

public class ImageUrlToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return new BitmapImage(new Uri(value.ToString() + "?param=70y70"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PausedToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is true) return "重试";
        return value is true ? "继续" : "暂停";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}