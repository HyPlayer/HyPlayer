#region

using HyPlayer.Classes;
using HyPlayer.Classes.LyricParser.Abstraction;
using HyPlayer.Controls;
using Kawazu;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
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
using Windows.UI.Xaml.Navigation;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Settings : Page
{
    private LyricItem _lyricItem;
    private bool isbyprogram;
    private int _elapse = 10;


    public static readonly DependencyProperty IsAdvancedLyricColorSettingsShowProperty = DependencyProperty.Register(
        "IsAdvancedLyricColorSettingsShow", typeof(bool), typeof(Settings), new PropertyMetadata(default(bool)));

    public bool IsAdvancedLyricColorSettingsShow
    {
        get => (bool)GetValue(IsAdvancedLyricColorSettingsShowProperty);
        set => SetValue(IsAdvancedLyricColorSettingsShowProperty, value);
    }

    public Settings()
    {
        isbyprogram = true;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {

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
        VersionCode.Text =
            $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}  (#{BuildInfo.CommitSha.Substring(0, 8)}@{BuildInfo.BuildBranchId})";
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
        FontBox.ItemsSource = GetAllFonts();
    }

    private List<FontInfo> GetAllFonts()
    {
        var names = CanvasTextFormat.GetSystemFontFamilies();
        var displayNames = CanvasTextFormat.GetSystemFontFamilies(new[] { "zh-cn" });
        var models = new List<FontInfo>();
        for (var i = 0; i < names.Length; i++)
        {
            models.Add(new FontInfo
            {
                Name = displayNames[i],
                Value = names[i]
            });
        }

        return models.OrderBy(t => t.Name).ToList();
    }

    public class FontInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    private async Task GetRomaji()
    {
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
        if (obj.Progress.TotalBytesToReceive == 0)
        {
            RomajiStatus.Header = "下载错误 " + obj.CurrentWebErrorStatus;
            return;
        }

        RomajiStatus.Header = $"正在下载资源文件 ({obj.Progress.BytesReceived * 100 / obj.Progress.TotalBytesToReceive:D}%)";
    }

    private async Task OnRomajiDownloadDone(DownloadOperation obj)
    {
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
        _ = GetRomaji();
    }

    private void ButtonXREALIPSave_OnClick(object sender, RoutedEventArgs e)
    {
        ApplicationData.Current.LocalSettings.Values["xRealIp"] =
            TextBoxXREALIP.Text == "" ? null : TextBoxXREALIP.Text;
        if (Common.NeteaseAPI != null)
        {
            Common.NeteaseAPI.Option.XRealIP = (string)ApplicationData.Current.LocalSettings.Values["xRealIp"];
        }
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
        _ = HistoryManagement.ClearHistory();
    }


    private void CopyDeviceCode_Click(object sender, RoutedEventArgs e)
    {
        var deviceInfo = new EasClientDeviceInformation();
        var dp = new DataPackage();
        dp.SetText(deviceInfo.Id.ToString());
        Clipboard.SetContent(dp);
    }

    private async void LyricSize_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (isbyprogram) return;
        await Task.Delay(20);
        _lyricItem.RefreshFontSize();
    }

    private void NBShadowDepth_OnValueChanged(object o, RangeBaseValueChangedEventArgs rangeBaseValueChangedEventArgs)
    {
        if (isbyprogram) return;
        var size = 4;
        if (int.TryParse(SliderAlbumShadowDepth.Value.ToString(), out size))
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
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("cacheFolder", folder);
            Common.Setting.cacheDir = folder.Path;
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

    private async void RestartBtn_Click(object sender, RoutedEventArgs e)
    {
        await CoreApplication.RequestRestartAsync("ChangeThemeRestart");
    }

    private void BtnXboxReserve_Click(object sender, RoutedEventArgs e)
    {
        Common.CollectGarbage();
    }

    private async void HotLyricOnStartUp_Checked(object sender, RoutedEventArgs e)
    {
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
        Common.Setting.AudioRenderDevice = "";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await UpdateManager.PopupVersionCheck();
    }

    private void ComboBoxSongBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isbyprogram) return;
        var selectedItem = (ComboBoxItem)((ComboBox)sender).SelectedItem;
        Common.Setting.audioRate = selectedItem.Tag.ToString();
    }

    private void ComboBoxSongDownloadBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isbyprogram) return;
        var selectedItem = (ComboBoxItem)((ComboBox)sender).SelectedItem;
        Common.Setting.downloadAudioRate = selectedItem.Tag.ToString();
    }

    private void CheckCanaryChannelButton_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateManager.GetUserCanaryChannelAvailability(canaryEmail.Text);
    }

    private async void ClearTileCache_Click(object sender, RoutedEventArgs e)
    {
        var storageFolder = await ApplicationData.Current.TemporaryFolder.TryGetItemAsync("LocalTileBackground");
        if (storageFolder != null) await storageFolder.DeleteAsync();
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
        if (isbyprogram) return;
        await Task.Delay(20);
        _lyricItem.RefreshFontSize();
    }

    private async void AboutRomaji_Click(object sender, RoutedEventArgs e)
    {
        await AboutRomajiDialog.ShowAsync();
    }

    private void DisplayMaintain_OnChecked(object sender, RoutedEventArgs e)
    {
        Common.DisplayRequest.RequestActive();
    }

    private void DisplayMaintain_OnUnchecked(object sender, RoutedEventArgs e)
    {
        Common.DisplayRequest.RequestRelease();
    }

    private async void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        await SimpleCacher.ClearAllCacheAsync();
    }
}