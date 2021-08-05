using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using HyPlayer.Classes;
using HyPlayer.Controls;
using Kawazu;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Microsoft.AppCenter.Crashes;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Settings : Page
    {
        private int _elapse = 10;

        private LyricItem _lyricItem;
        private bool isbyprogram;

        public Settings()
        {
            isbyprogram = true;
            InitializeComponent();
            RomajiStatus.Text = "当前日语转罗马音状态: " + (Common.KawazuConv == null ? "无法转换 请尝试重新下载资源文件" : "可以转换");
            RadioButtonsSongBr.SelectedIndex =
                RadioButtonsSongBr.Items.IndexOf(RadioButtonsSongBr.Items.First(t =>
                    ((RadioButton)t).Tag.ToString() == Common.Setting.audioRate));
            TextBoxDownloadDir.Text = Common.Setting.downloadDir;
            LazySongUrlGetCheck.IsChecked = Common.Setting.songUrlLazyGet;
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
            EasClientDeviceInformation deviceInfo = new EasClientDeviceInformation();
            DeviceInfo.Text = "设备识别码: " + deviceInfo.FriendlyName;
            _lyricItem = new LyricItem(new SongLyric
            {
                PureLyric = "歌词大小示例 AaBbCcDd 約束の言葉",
                Translation = "翻译大小示例",
                HaveTranslation = true
            });
            _lyricItem.OnShow();
            CheckBoxAlignment.IsChecked = Common.Setting.lyricAlignment;
            StackPanelLyricSet.Children.Add(_lyricItem);
            LyricSize.Value = Common.Setting.lyricSize;
            RomajiSize.Value = Common.Setting.romajiSize;
            NBShadowDepth.Value  =Common.Setting.expandedCoverShadowDepth;
            RadioButtonsTheme.SelectedIndex = Common.Setting.themeRequest;
            isbyprogram = false;
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
                    var dl = downloader.CreateDownload(new Uri("https://api.kengwang.co/hyplayer/getromaji.php"), sf);
                    HandleDownloadAsync(dl, true);
                });
            });
        }

        private async void HandleDownloadAsync(DownloadOperation dl, bool b)
        {
            var process = new Progress<DownloadOperation>(ProgressCallback);
            await dl.StartAsync().AsTask(process);
        }

        private void ProgressCallback(DownloadOperation obj)
        {
            RomajiStatus.Text = $"正在下载资源文件 ({obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive:D}%)";
            if (obj.Progress.BytesReceived == obj.Progress.TotalBytesToReceive)
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
                Common.Setting.downloadDir = folder.Path;
                TextBoxDownloadDir.Text = Common.Setting.downloadDir;
            }
        }


        private void UIElement_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
        {
            if (_elapse-- == 0) ApplicationData.Current.RoamingSettings.Values["CanDownload"] = true;
        }

        private void LazySongUrlGetCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.songUrlLazyGet = true;
        }

        private void LazySongUrlGetCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.songUrlLazyGet = false;
        }

        private void ControlSoundChecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            ElementSoundPlayer.State = ElementSoundPlayerState.On;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.On;
        }

        private void ControlSoundUnChecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            HistoryManagement.ClearHistory();
        }

        private void LyricSize_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (isbyprogram) return;
            int size = 18;
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
            int size = 4;
            if (int.TryParse(NBShadowDepth.Value.ToString(), out size))
            {
                Common.Setting.expandedCoverShadowDepth = Math.Max(0, size);
            }
        }

        private void CheckBoxAlignment_OnChecked(object sender, RoutedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.lyricAlignment = CheckBoxAlignment.IsChecked != null && CheckBoxAlignment.IsChecked.Value;
            _lyricItem.RefreshFontSize();
        }

        private void RadioButtonsTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isbyprogram) return;
            Common.Setting.themeRequest = RadioButtonsTheme.SelectedIndex;
        }
    }
}