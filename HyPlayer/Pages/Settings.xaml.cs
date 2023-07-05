#region

using HyPlayer.Classes;
using HyPlayer.Controls;
using Kawazu;
using LyricParser.Abstraction;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.BackgroundTransfer;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Settings : Page, IDisposable
{
    private readonly LyricItem _lyricItem;
    private readonly bool isbyprogram;
    private int _elapse = 10;
    private bool disposedValue = false;

    public Settings()
    {
        isbyprogram = true;
        InitializeComponent();
        RomajiStatus.Header = (Common.KawazuConv == null ? "请下载Kawazu资源文件" : "可以转换");
        ButtonDownloadRomaji.Visibility = Common.KawazuConv == null ? Visibility.Visible : Visibility.Collapsed;
        if (Common.Setting.audioRate.EndsWith('0') || Common.Setting.downloadAudioRate.EndsWith('0'))
        {
            Common.Setting.audioRate = "exhigh";
            Common.Setting.downloadAudioRate = "hires";
        }
        else
        {
            ComboBoxSongBr.SelectedIndex = ComboBoxSongBr.Items.IndexOf(ComboBoxSongBr.Items.First(t =>
                ((ComboBoxItem)t).Tag.ToString() == Common.Setting.audioRate));
            ComboBoxSongDownloadBr.SelectedIndex = ComboBoxSongDownloadBr.Items.IndexOf(
                ComboBoxSongDownloadBr.Items.First(t =>
                    ((ComboBoxItem)t).Tag.ToString() == Common.Setting.downloadAudioRate));
        }

        TextBoxXREALIP.Text = ApplicationData.Current.LocalSettings.Values["xRealIp"] != null
            ? ApplicationData.Current.LocalSettings.Values["xRealIp"].ToString()
            : "";
        var package = Package.Current;
        var packageId = package.Id;
        var version = packageId.Version;
        VersionCode.Text = string.Format("Version {0}.{1}.{2}.{3}  (Package ID: {4})", version.Major, version.Minor,
            version.Build, version.Revision, packageId.Name);
        var deviceInfo = new EasClientDeviceInformation();
        DeviceInfo.Text = deviceInfo.Id.ToString();
        _lyricItem = new LyricItem(new SongLyric
        {
            LyricLine = new LrcLyricsLine("歌词大小示例 AaBbCcDd 約束の言葉", TimeSpan.Zero),
            Translation = "翻译大小示例"
        });
        _lyricItem.Transitions.Add(new RepositionThemeTransition());
        _lyricItem.IsHitTestVisible = false;
        _lyricItem.OnShow();
        StackPanelLyricSet.Children.Add(_lyricItem);
        isbyprogram = false;
#if DEBUG
        VersionCode.Text += " Debug";
#endif
        //ToggleButtonDaylight.IsChecked = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        BtnXboxReserve.Visibility = true ? Visibility.Visible : Visibility.Collapsed;
    }
    public static readonly DependencyProperty IsAdvancedLyricColorSettingsShowProperty = DependencyProperty.Register(
        "IsAdvancedLyricColorSettingsShow", typeof(bool), typeof(Settings), new PropertyMetadata(default(bool)));

    public bool IsAdvancedLyricColorSettingsShow
    {
        get => (bool)GetValue(IsAdvancedLyricColorSettingsShowProperty);
        set => SetValue(IsAdvancedLyricColorSettingsShowProperty, value);
    }

    private async Task GetRomaji()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        RomajiStatus.Header = "正在下载资源文件 请稍等";
        try
        {
            var undeletedRomajiFile = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync("RomajiData.zip");
            if (undeletedRomajiFile != null) await undeletedRomajiFile.DeleteAsync();
        }
        catch
        {
            // ignored
        }

        var sf = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("RomajiData.zip");
        var downloader = new BackgroundDownloader();
        var dl = downloader.CreateDownload(new Uri("https://api.kengwang.com.cn/hyplayer/getromaji.php"),
            sf);
        _ = HandleDownloadAsync(dl, true);
    }

    private async Task HandleDownloadAsync(DownloadOperation dl, bool b)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var process = new Progress<DownloadOperation>(ProgressCallback);
        try
        {
            await dl.StartAsync().AsTask(process);
            if (dl.Progress.TotalBytesToReceive > 5000) _ = OnRomajiDownloadDone(dl);
        }
        catch (Exception E)
        {
            RomajiStatus.Header = "下载错误 " + E.Message;
        }
    }

    private void ProgressCallback(DownloadOperation obj)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (obj.Progress.TotalBytesToReceive == 0)
        {
            RomajiStatus.Header = "下载错误 " + obj.CurrentWebErrorStatus;
            return;
        }

        RomajiStatus.Header = $"正在下载资源文件 ({obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive:D}%)";
    }

    private async Task OnRomajiDownloadDone(DownloadOperation obj)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        try
        {
            //下载完成
            //unzip
            RomajiStatus.Header = "正在解压,请稍等......";
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
            RomajiStatus.Header = "罗马字文件解压错误: " + e.Message;
        }
        finally
        {
            RomajiStatus.Header =
                (Common.KawazuConv == null ? "请重新下载资源文件" : "可以转换");
            ButtonDownloadRomaji.Visibility = Common.KawazuConv != null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        _ = GetRomaji();
    }

    private void ButtonXREALIPSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        ApplicationData.Current.LocalSettings.Values["xRealIp"] =
            TextBoxXREALIP.Text == "" ? null : TextBoxXREALIP.Header;
        if (Common.ncapi != null)
        {
            Common.ncapi.RealIP = (string)ApplicationData.Current.LocalSettings.Values["xRealIp"];
        }
    }

    private async void ButtonDownloadSelect_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("downloadFolder", folder);
            Common.Setting.downloadDir = folder.Path;
        }
    }

    private async void ButtonSearchingSelect_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("searchingFolder", folder);
            Common.Setting.searchingDir = folder.Path;
        }
    }


    private void UIElement_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (_elapse-- <= 0) Common.NavigatePage(typeof(TestPage));
    }


    private void ControlSoundChecked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        Common.Setting.uiSound = true;
        ElementSoundPlayer.State = ElementSoundPlayerState.On;
        ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.On;
    }

    private void ControlSoundUnChecked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        Common.Setting.uiSound = false;
        ElementSoundPlayer.State = ElementSoundPlayerState.Off;
        ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        _ = HistoryManagement.ClearHistory();
    }


    private void CopyDeviceCode_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var deviceInfo = new EasClientDeviceInformation();
        var dp = new DataPackage();
        dp.SetText(deviceInfo.Id.ToString());
        Clipboard.SetContent(dp);
    }

    private async void LyricSize_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        await Task.Delay(20);
        _lyricItem.RefreshFontSize();
    }

    private void NBShadowDepth_OnValueChanged(object o, RangeBaseValueChangedEventArgs rangeBaseValueChangedEventArgs)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        var size = 4;
        if (int.TryParse(SliderAlbumShadowDepth.Value.ToString(), out size))
            Common.Setting.expandedCoverShadowDepth = Math.Max(0, size);
    }


    private async void ButtonCacheSelect_OnClick(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("cacheFolder", folder);
            Common.Setting.cacheDir = folder.Path;
        }
    }

    private void StackPanel_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        _elapse -= 2;
        if (_elapse <= 0) ApplicationData.Current.RoamingSettings.Values["CanDownload"] = true;
    }

    private void DeviceInfo_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        DeviceInfo.ContextFlyout.ShowAt(DeviceInfo);
    }

    private async void RestartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        await CoreApplication.RequestRestartAsync("ChangeThemeRestart");
    }

    private void BtnXboxReserve_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        Common.CollectGarbage();
    }

    private async void HotLyricOnStartUp_Checked(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        try
        {
            var uri = new Uri($"hot-lyric:///?from={Package.Current.Id.FamilyName}");
            if (await Launcher.QueryUriSupportAsync(uri, LaunchQuerySupportType.Uri) !=
                LaunchQuerySupportStatus.Available)
            {
                var dlg = new ContentDialog
                {
                    Title = "当前未安装 「热词」",
                    Content = "是否前往商店安装 「热词」",
                    CloseButtonText = "否",
                    PrimaryButtonText = "安装「热词」"
                };

                var res = await dlg.ShowAsync(ContentDialogPlacement.Popup);
                if (res == ContentDialogResult.Primary)
                {
                    await Launcher.LaunchUriAsync(new Uri("ms-windows-store://pdp?productId=9MXFFHVQVBV9"));
                    return;
                }

                Common.Setting.hotlyricOnStartup = false;
            }
            else
            {
                await Launcher.LaunchUriAsync(uri);
            }
        }
        catch
        {
        }
    }

    private async void BtnChangeAudioRenderDevice_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var devicePicker = new DevicePicker();
        devicePicker.Filter.SupportedDeviceClasses.Add(DeviceClass.AudioRender);
        var ge = BtnChangeAudioRenderDevice.TransformToVisual(null);
        var point = ge.TransformPoint(new Point());
        var rect = new Rect(point,
            new Point(point.X + BtnChangeAudioRenderDevice.ActualWidth,
                point.Y + BtnChangeAudioRenderDevice.ActualHeight));
        var device = await devicePicker.PickSingleDeviceAsync(rect);
        if (device != null) Common.Setting.AudioRenderDevice = device.Id;
    }

    private void BtnChangeToDefaultAudioRenderDevice_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        Common.Setting.AudioRenderDevice = "";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        await UpdateManager.PopupVersionCheck();
    }

    private void ComboBoxSongBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        var selectedItem = (ComboBoxItem)((ComboBox)sender).SelectedItem;
        Common.Setting.audioRate = selectedItem.Tag.ToString();
    }

    private void ComboBoxSongDownloadBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        var selectedItem = (ComboBoxItem)((ComboBox)sender).SelectedItem;
        Common.Setting.downloadAudioRate = selectedItem.Tag.ToString();
    }

    private void CheckCanaryChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        _ = UpdateManager.GetUserCanaryChannelAvailability(canaryEmail.Text);
    }

    private async void ClearTileCache_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var storageFolder = await ApplicationData.Current.TemporaryFolder.TryGetItemAsync("LocalTileBackground");
        if (storageFolder != null) await storageFolder.DeleteAsync();
    }

    private async void LoginLastFMAccount_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        var LoginBox = new LastFMLoginPage();
        await LoginBox.ShowAsync();
    }

    private void LogoffLastFMAccount_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        LastFMManager.TryLogoffLastFM();
    }

    private void ResetPureLyricIdleColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.pureLyricIdleColor = null;
    }

    private void ConfirmPureLyricIdleColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.pureLyricIdleColor = PureLyricIdle.SelectedColor;
    }

    private void ResetPureLyricFocusingColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.pureLyricFocusingColor = null;
    }

    private void ConfirmPureLyricFocusingColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.pureLyricFocusingColor = PureLyricFocusing.SelectedColor;
    }

    private void ResetKaraokLyricFocusingColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.karaokLyricFocusingColor = null;
    }

    private void ConfirmKaraokLyricFocusingColor(object sender, RoutedEventArgs e)
    {
        Common.Setting.karaokLyricFocusingColor = KaraokLyricFocusing.SelectedColor;
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                StackPanelLyricSet.Children.Clear();
            }
            disposedValue = true;
        }
    }

    ~Settings()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void ApplyNewAcrylic()
    {
        var Brush = new Microsoft.UI.Xaml.Media.AcrylicBrush()
        {
            BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
            TintColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
            TintOpacity = TintOpacitySlider.Value,
            TintLuminosityOpacity = TintOpacityLuminositySlider.Value,
            FallbackColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
        };
        PreviewAcrylic.Fill = Brush;
    }

    private void TintOpacity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyNewAcrylic();
    }

    private void TintOpacityLuminosity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyNewAcrylic();
    }

    private async void LyricAlignment_OnToggled(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(Settings));
        if (isbyprogram) return;
        await Task.Delay(20);
        _lyricItem.RefreshFontSize();
    }

    private async void AboutRomaji_Click(object sender, RoutedEventArgs e)
    {

        await AboutRomajiDialog.ShowAsync();
        
    }
}
