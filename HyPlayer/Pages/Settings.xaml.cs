using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using HyPlayer.HyPlayControl;
using Kawazu;
using Windows.ApplicationModel;

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
            TextBoxDownloadDir.Text = Common.Setting.downloadDir;
            ToastLyricCheckbox.IsChecked = Common.Setting.toastLyric;
            AnimationCheckbox.IsChecked = Common.Setting.expandAnimation;
            LazySongUrlGetCheck.IsChecked = ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] != null && ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"].ToString() != "false";
            Package package = Package.Current;
            PackageId packageId = package.Id;
            PackageVersion version = packageId.Version;
            VersionCode.Text = string.Format("Version {0}.{1}.{2}.{3}  (Package ID: {4})", version.Major, version.Minor, version.Build, version.Revision, packageId.Name);
            if (version.Revision != 0)
            {
                VersionCode.Text += " Preview";
            }
#if DEBUG
            VersionCode.Text += " Debug";
#endif
            //ToggleButtonDaylight.IsChecked = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            Common.Setting.audioRate = ((RadioButton)sender).Tag.ToString();
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

        private async void ButtonDownloadSelect_OnClick(object sender, RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            Windows.Storage.StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                Common.Setting.downloadDir = folder.Path;
                TextBoxDownloadDir.Text = Common.Setting.downloadDir;
            }
        }

        private void ToastLyricCheckbox_OnChecked(object sender, RoutedEventArgs e)
        {
            Common.Setting.toastLyric = ToastLyricCheckbox.IsChecked.Value;
            Common.Setting.expandAnimation = AnimationCheckbox.IsChecked.Value;
            Common.BarPlayBar.InitializeDesktopLyric();

        }


        private int _elapse = 10;
        private void UIElement_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_elapse-- == 0) ApplicationData.Current.RoamingSettings.Values["CanDownload"] = true;
        }

        private void LazySongUrlGetCheck_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] = "true";
        }

        private void LazySongUrlGetCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["songUrlLazyGet"] = "false";
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["songlistHistory"] = null;
            ApplicationData.Current.LocalSettings.Values["songHistory"] = null;
        }
    }
}
