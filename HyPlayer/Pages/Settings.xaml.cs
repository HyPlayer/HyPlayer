#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using Kawazu;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Networking.BackgroundTransfer;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Settings : Page
    {
        private int _elapse = 10;

        private readonly LyricItem _lyricItem;
        private readonly bool isbyprogram;

        public Settings()
        {
            isbyprogram = true;
            InitializeComponent();
            RomajiStatus.Text = "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
            RadioButtonsSongBr.SelectedIndex =
                RadioButtonsSongBr.Items.IndexOf(RadioButtonsSongBr.Items.First(t =>
                    ((RadioButton)t).Tag.ToString() == Common.Setting.audioRate));
            RadioButtonsSongDownloadBr.SelectedIndex = RadioButtonsSongDownloadBr.Items.IndexOf(RadioButtonsSongDownloadBr.Items.First(t =>
                    ((RadioButton)t).Tag.ToString() == Common.Setting.downloadAudioRate));
            TextBoxXREALIP.Text = ApplicationData.Current.LocalSettings.Values["xRealIp"] != null
                ? ApplicationData.Current.LocalSettings.Values["xRealIp"].ToString()
                : "";
            TextBoxPROXY.Text = ApplicationData.Current.LocalSettings.Values["neteaseProxy"] != null
                ? ApplicationData.Current.LocalSettings.Values["neteaseProxy"].ToString()
                : "";
            var package = Package.Current;
            var packageId = package.Id;
            var version = packageId.Version;
            VersionCode.Text = string.Format("Version {0}.{1}.{2}.{3}  (Package ID: {4})", version.Major, version.Minor,
                version.Build, version.Revision, packageId.Name);
            if (version.Revision != 0) VersionCode.Text += " Preview";
            var deviceInfo = new EasClientDeviceInformation();
            DeviceInfo.Text = "设备识别码: " + deviceInfo.Id;
            _lyricItem = new LyricItem(new SongLyric
            {
                PureLyric = "歌词大小示例 AaBbCcDd 約束の言葉",
                Translation = "翻译大小示例",
                HaveTranslation = true
            });
            _lyricItem.OnShow();
            StackPanelLyricSet.Children.Add(_lyricItem);
            isbyprogram = false;
#if DEBUG
            VersionCode.Text += " Debug";
#endif
            //ToggleButtonDaylight.IsChecked = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            BtnXboxReserve.Visibility = true ? Visibility.Visible : Visibility.Collapsed;
        }


        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.audioRate = ((RadioButton)sender).Tag.ToString();
        }

        private void RadioButton1_Checked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.downloadAudioRate = ((RadioButton)sender).Tag.ToString();
        }

        private void GetRomaji()
        {
            Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    RomajiStatus.Text = "正在下载资源文件 请稍等";
                    try
                    {
                        await (await ApplicationData.Current.LocalCacheFolder.GetFileAsync("RomajiData.zip"))
                            .DeleteAsync();
                    }
                    catch
                    {
                    }

                    var sf = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("RomajiData.zip");
                    var downloader = new BackgroundDownloader();
                    var dl = downloader.CreateDownload(new Uri("https://api.kengwang.com.cn/hyplayer/getromaji.php"),
                        sf);
                    HandleDownloadAsync(dl, true);
                });
            });
        }

        private async void HandleDownloadAsync(DownloadOperation dl, bool b)
        {
            var process = new Progress<DownloadOperation>(ProgressCallback);
            try
            {
                await dl.StartAsync().AsTask(process);
            }
            catch (Exception E)
            {
                RomajiStatus.Text = "下载错误 " + E.Message;
            }
        }

        private void ProgressCallback(DownloadOperation obj)
        {
            if (obj.Progress.TotalBytesToReceive == 0)
            {
                RomajiStatus.Text = "下载错误 " + obj.CurrentWebErrorStatus;
                return;
            }

            RomajiStatus.Text = $"正在下载资源文件 ({obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive:D}%)";
            if (obj.Progress.BytesReceived == obj.Progress.TotalBytesToReceive &&
                obj.Progress.TotalBytesToReceive > 5000)
                _ = Task.Run(() =>
                {
                    Common.Invoke(async () =>
                    {
                        try
                        {
                            //下载完成
                            //unzip
                            RomajiStatus.Text = "正在解压,请稍等......";
                            await Task.Delay(1000);
                            var path =
                                (await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Romaji",
                                    CreationCollisionOption.OpenIfExists)).Path;
                            //Read the file stream
                            var a = await obj.ResultFile.OpenStreamForReadAsync();

                            //unzip
                            var archive = new ZipArchive(a);
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
                            RomajiStatus.Text =
                                "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
                        }
                    });
                });
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            GetRomaji();
        }

        private void ButtonXREALIPSave_OnClick(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["xRealIp"] =
                TextBoxXREALIP.Text == "" ? null : TextBoxXREALIP.Text;
            Common.ncapi.RealIP = (string)ApplicationData.Current.LocalSettings.Values["xRealIp"];
        }


        private void ButtonPROXYSave_OnClick(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["neteaseProxy"] =
                TextBoxPROXY.Text == "" ? null : TextBoxPROXY.Text;
            Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
            Common.ncapi.Proxy = new WebProxy((string)ApplicationData.Current.LocalSettings.Values["neteaseProxy"]);
        }

        private async void ButtonDownloadSelect_OnClick(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("downloadFolder", folder);
                Common.Setting.downloadDir = folder.Path;
                TextBoxDownloadDir.Text = Common.Setting.downloadDir;
            }
        }
        private async void ButtonSearchingSelect_OnClick(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("searchingFolder", folder);
                Common.Setting.searchingDir = folder.Path;
                TextBoxSearchingDir.Text = Common.Setting.searchingDir;
            }
        }


        private void UIElement_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            if (_elapse-- <= 0) Common.NavigatePage(typeof(TestPage));
        }


        private void ControlSoundChecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.uiSound = true;
            ElementSoundPlayer.State = ElementSoundPlayerState.On;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.On;
        }

        private void ControlSoundUnChecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.uiSound = false;
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            HistoryManagement.ClearHistory();
        }


        private void CopyDeviceCode_Click(object sender, RoutedEventArgs e)
        {
            var deviceInfo = new EasClientDeviceInformation();
            var dp = new DataPackage();
            dp.SetText(deviceInfo.Id.ToString());
            Clipboard.SetContent(dp);
        }

        private void LyricSize_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (isbyprogram) return;
            var size = 18;
            if (int.TryParse(LyricSize.Text, out size))
            {
                Common.Setting.lyricSize = size;
                _lyricItem.RefreshFontSize();
            }

            size = 15;
            if (int.TryParse(RomajiSize?.Text, out size))
            {
                size = Math.Max(size, 1);
                Common.Setting.romajiSize = size;
                _lyricItem.RefreshFontSize();
            }
        }

        private void NBShadowDepth_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (isbyprogram) return;
            var size = 4;
            if (int.TryParse(NBShadowDepth.Value.ToString(), out size))
                Common.Setting.expandedCoverShadowDepth = Math.Max(0, size);
        }


        private async void ButtonCacheSelect_OnClick(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                Common.Setting.cacheDir = folder.Path;
                TextBoxCacheDir.Text = Common.Setting.cacheDir;
            }
        }

        private void StackPanel_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            _elapse -= 2;
            if (_elapse <= 0) ApplicationData.Current.RoamingSettings.Values["CanDownload"] = true;
        }

        private void DeviceInfo_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            DeviceInfo.ContextFlyout.ShowAt(DeviceInfo);
        }

        private void RadioButtonsTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RestartBtn.Visibility = Visibility.Visible;
        }

        private async void RestartBtn_Click(object sender, RoutedEventArgs e)
        {
            await CoreApplication.RequestRestartAsync("ChangeThemeRestart");
        }

        private void BtnXboxReserve_Click(object sender, RoutedEventArgs e)
        {
            Common.CollectGarbage();
        }
    }
}