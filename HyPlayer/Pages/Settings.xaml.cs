using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Kawazu;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Settings : Page
    {
        public Settings()
        {
            InitializeComponent();
            RomajiStatus.Text = "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Common.Setting.bitrate = ((RadioButton)sender).Tag.ToString();
        }

        private void GetRomaji()
        {
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    RomajiStatus.Text = "正在下载资源文件";
                    StorageFile sf = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("RomajiData.zip");
                    var downloader = new BackgroundDownloader();
                    var dl = downloader.CreateDownload(new Uri("https://api.kengwang.co/hyplayer/getromaji.php"), sf);
                    HandleDownloadAsync(dl, true);
                }));

            }));
        }

        private async void HandleDownloadAsync(DownloadOperation dl, bool b)
        {
            var process = new Progress<DownloadOperation>(ProgressCallback);
            await dl.StartAsync().AsTask(process);
        }

        private async void ProgressCallback(DownloadOperation obj)
        {
            if (obj.Progress.BytesReceived >= obj.Progress.TotalBytesToReceive)
            {
                //下载完成
                Stream a = await obj.ResultFile.OpenStreamForReadAsync();
                //unzip
                ZipArchive archive = new ZipArchive(a);
                string path = (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Romaji")).Path;
                archive.ExtractToDirectory(path);
                _ = obj.ResultFile.DeleteAsync();
                try
                {
                    Common.KawazuConv = new KawazuConverter(path);
                }
                catch (Exception) { }
                RomajiStatus.Text = "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
            }
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            GetRomaji();
        }
    }
}
