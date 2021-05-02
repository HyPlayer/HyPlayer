﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Kawazu;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;

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
            RadioButtonsSongBr.SelectedIndex =
                RadioButtonsSongBr.Items.IndexOf(RadioButtonsSongBr.Items.First(t => ((RadioButton)t).Tag.ToString() == Common.Setting.audioRate));
            bool IsFDOn = Common.Setting.FDOption;
            Acrylic.IsChecked = IsFDOn;
            Opaque.IsChecked = !Acrylic.IsChecked;

            //ToggleButtonDaylight.IsChecked = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            bool IsFDOn = Common.Setting.FDOption;
            if (IsFDOn)
                this.Background = Application.Current.Resources["SystemControlAcrylicWindowBrush"] as Brush;
            else this.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
        }
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Common.Setting.audioRate = ((RadioButton)sender).Tag.ToString();
        }
        private void FDPicker_Checked(object sender, RoutedEventArgs e)
        {
            string tmp = ((RadioButton)sender).Tag.ToString();
            if (tmp == "No")
                Common.Setting.FDOption = false;
            else Common.Setting.FDOption = true;
        }

        private void GetRomaji()
        {
            Task.Run((() =>
            {
                Common.Invoke((async () =>
                {
                    RomajiStatus.Text = "正在下载资源文件 请稍等";
                    try
                    {
                        await (await ApplicationData.Current.LocalCacheFolder.GetFileAsync("RomajiData.zip")).DeleteAsync();
                    }
                    catch { }
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

        private void ProgressCallback(DownloadOperation obj)
        {
            RomajiStatus.Text = $"正在下载资源文件 ({((obj.Progress.BytesReceived * 100) / obj.Progress.TotalBytesToReceive):D}%)";
            if (obj.Progress.BytesReceived == obj.Progress.TotalBytesToReceive)
            {
                _ = Task.Run((() =>
                {
                    Common.Invoke((async () =>
                    {
                        try
                        {
                            //下载完成
                            //unzip
                            RomajiStatus.Text = "正在解压,请稍等......";
                            await Task.Delay(1000);
                            string path =
                                (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Romaji",
                                    CreationCollisionOption.OpenIfExists)).Path;
                            //Read the file stream
                            Stream a = await obj.ResultFile.OpenStreamForReadAsync();

                            //unzip
                            ZipArchive archive = new ZipArchive(a);
                            archive.ExtractToDirectory(path);
                            _ = obj.ResultFile.DeleteAsync();

                            Common.KawazuConv = new KawazuConverter(path);
                        }
                        catch (Exception e)
                        {
                            RomajiStatus.Text = "罗马音文件解压错误: " + e.Message;
                        }
                        finally
                        {
                            RomajiStatus.Text = "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
                        }
                    }));
                }));

            }
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            GetRomaji();
        }


    }
}
