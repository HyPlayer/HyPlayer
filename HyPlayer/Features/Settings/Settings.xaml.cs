#region

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
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Settings.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.Shell;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Dialogs;
using Kawazu;
using LiteFM;
using Microsoft.Graphics.Canvas.Text;
using WinRT;
using Point = Windows.Foundation.Point;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Settings;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class Settings : Page
{
    private static readonly string[] _localeList = ["zh-cn"];
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    private readonly IPlaybackControlService _playbackControl =
        Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private readonly IPlaybackMemoryService _playbackMemory = Ioc.Default.GetRequiredService<IPlaybackMemoryService>();

    private readonly IProviderNetworkConfigurationProvidable _providerNetworkConfiguration =
        Ioc.Default.GetRequiredService<IProviderNetworkConfigurationProvidable>();

    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly ITileService _tileService = Ioc.Default.GetRequiredService<ITileService>();
    private int _elapse = 10;

    private bool _isUpdatingControls;

    public PlaybackSettings Playback { get; } = Ioc.Default.GetRequiredService<PlaybackSettings>();
    public UISettings UI { get; } = Ioc.Default.GetRequiredService<UISettings>();
    public ApiSettings Api { get; } = Ioc.Default.GetRequiredService<ApiSettings>();
    public LyricSettings Lyric { get; } = Ioc.Default.GetRequiredService<LyricSettings>();
    public LastFMSettings LastFM { get; } = Ioc.Default.GetRequiredService<LastFMSettings>();
    public DownloadSettings Download { get; } = Ioc.Default.GetRequiredService<DownloadSettings>();
    public LocalLibrarySettings LocalLibrary { get; } = Ioc.Default.GetRequiredService<LocalLibrarySettings>();
    public SettingsViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<SettingsViewModel>();

    public Settings()
    {
        _isUpdatingControls = true;
        InitializeComponent();
    }

    private void TransitionMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransitionMode.SelectedValue is not string transitionId
            || transitionId is not ("dir" or "gap" or "xfd")
            || transitionId == Playback.TransitionId)
            return;

        Playback.TransitionId = transitionId;
        _taskRunner.Forget(
            _playbackControl.SetTransitionAsync(transitionId),
            "change track transition");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var kawazu = Ioc.Default.GetRequiredService<IKawazuStateService>();
        RomajiStatus.Header = kawazu.Converter == null ? "请下载Kawazu资源文件" : "可以转换";
        ButtonDownloadRomaji.Visibility = kawazu.Converter == null ? Visibility.Visible : Visibility.Collapsed;
        if (Playback.AudioRate.EndsWith('0') || Download.DownloadAudioRate.EndsWith('0'))
        {
            Playback.AudioRate = "exhigh";
            Download.DownloadAudioRate = "hires";
        }
        else
        {
            ComboBoxSongBr.SelectedIndex = ComboBoxSongBr.Items.IndexOf(ComboBoxSongBr.Items.First(t =>
                t?.As<ComboBoxItem>().Tag.ToString() == Playback.AudioRate));
            ComboBoxSongDownloadBr.SelectedIndex = ComboBoxSongDownloadBr.Items.IndexOf(
                ComboBoxSongDownloadBr.Items.First(t =>
                    t?.As<ComboBoxItem>().Tag.ToString() == Download.DownloadAudioRate));
        }

        TextBoxXREALIP.Text = Api.RealIp ?? "";
        var package = Package.Current;
        var packageId = package.Id;
        var version = packageId.Version;
        VersionCode.Text =
            $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}  (#{BuildInfo.CommitSha[..7]}@{BuildInfo.BuildBranchId})";
        var deviceInfo = new EasClientDeviceInformation();
        DeviceInfo.Text = deviceInfo.Id.ToString();
        _isUpdatingControls = false;
#if DEBUG
        VersionCode.Text += " Debug";
#endif
        FontBox.ItemsSource = GetAllFonts();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // This page binds to singleton settings objects. The generated x:Bind
        // listeners otherwise stay registered until their weak targets are
        // observed as dead by a later settings change.
        Bindings.StopTracking();
        FontBox.ItemsSource = null;
    }

    private static List<FontInfo> GetAllFonts()
    {
        var names = CanvasTextFormat.GetSystemFontFamilies();
        var displayNames = CanvasTextFormat.GetSystemFontFamilies(_localeList);
        var models = new List<FontInfo>();
        for (var i = 0; i < names.Length; i++)
            models.Add(new FontInfo
            {
                Name = displayNames[i],
                Value = names[i]
            });

        return [.. models.OrderBy(t => t.Name)];
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
        _ = HandleDownloadAsync(dl);
    }

    private async Task HandleDownloadAsync(DownloadOperation dl)
    {
        var process = new Progress<DownloadOperation>(ProgressCallback);
        try
        {
            await dl.StartAsync().AsTask(process);
            if (dl.Progress.TotalBytesToReceive > 5000) _ = OnRomajiDownloadDone(dl);
        }
        catch (Exception e)
        {
            RomajiStatus.Header = "下载错误 " + e.Message;
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
            var path =
                (await ApplicationData.Current.LocalFolder.CreateFolderAsync("Romaji",
                    CreationCollisionOption.OpenIfExists)).Path;
            //Read the file stream
            var a = await obj.ResultFile.OpenStreamForReadAsync();

            //unzip
            var archive = new ZipArchive(a);
            archive.ExtractToDirectory(path);
            _ = obj.ResultFile.DeleteAsync();

            Ioc.Default.GetRequiredService<IKawazuStateService>().Converter = new KawazuConverter(path);
        }
        catch (Exception e)
        {
            RomajiStatus.Header = "罗马字文件解压错误: " + e.Message;
        }
        finally
        {
            var kawazu = Ioc.Default.GetRequiredService<IKawazuStateService>();
            RomajiStatus.Header =
                kawazu.Converter == null ? "请重新下载资源文件" : "可以转换";
            ButtonDownloadRomaji.Visibility = kawazu.Converter == null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        _ = GetRomaji();
    }

    private void ButtonXREALIPSave_OnClick(object sender, RoutedEventArgs e)
    {
        var xRealIp = string.IsNullOrEmpty(TextBoxXREALIP.Text) ? null : TextBoxXREALIP.Text;
        Api.RealIp = xRealIp;
        _providerNetworkConfiguration.ConfigureClientNetwork(xRealIp, Api.UseHttp);
    }

    private async void ButtonDownloadSelect_OnClick(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("downloadFolder", folder);
            Download.DownloadDirectory = folder.Path;
        }
    }

    private async void ButtonSearchingSelect_OnClick(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("searchingFolder", folder);
            LocalLibrary.SearchDirectory = folder.Path;
        }
    }


    private void UIElement_OnTapped(object sender, TappedRoutedEventArgs tappedRoutedEventArgs)
    {
        if (_elapse-- <= 0) _navigation.Navigate(typeof(TestPage));
    }


    private void ControlSoundChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingControls) return;
        UI.UISound = true;
        ElementSoundPlayer.State = ElementSoundPlayerState.On;
        ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.On;
    }

    private void ControlSoundUnChecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingControls) return;
        UI.UISound = false;
        ElementSoundPlayer.State = ElementSoundPlayerState.Off;
        ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
    }


    private void CopyDeviceCode_Click(object sender, RoutedEventArgs e)
    {
        var deviceInfo = new EasClientDeviceInformation();
        var dp = new DataPackage();
        dp.SetText(deviceInfo.Id.ToString());
        Clipboard.SetContent(dp);
    }

    private void NBShadowDepth_OnValueChanged(object o, RangeBaseValueChangedEventArgs rangeBaseValueChangedEventArgs)
    {
        if (_isUpdatingControls) return;
        var size = (int)SliderAlbumShadowDepth.Value;
        UI.ExpandedCoverShadowDepth = Math.Max(0, size);
    }


    private async void ButtonCacheSelect_OnClick(object sender, RoutedEventArgs e)
    {
        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop
        };
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("cacheFolder", folder);
            Playback.CacheDirectory = folder.Path;
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

                Lyric.HotLyricOnStartup = false;
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
        if (device != null) Playback.AudioRenderDevice = device.Id;
    }

    private void BtnChangeToDefaultAudioRenderDevice_Click(object sender, RoutedEventArgs e)
    {
        Playback.AudioRenderDevice = "";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await UpdateManager.PopupVersionCheck();
    }

    private void ComboBoxSongBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingControls) return;
        var selectedItem = sender?.As<ComboBox>().SelectedItem?.As<ComboBoxItem>();
        Playback.AudioRate = selectedItem.Tag.ToString();
    }

    private void ComboBoxSongDownloadBr_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingControls) return;
        var selectedItem = sender?.As<ComboBox>().SelectedItem?.As<ComboBoxItem>();
        Download.DownloadAudioRate = selectedItem.Tag.ToString();
    }

    private void CheckCanaryChannelButton_Click(object sender, RoutedEventArgs e)
    {
        _ = UpdateManager.GetUserCanaryChannelAvailability(canaryEmail.Text);
    }


    private async void AboutRomaji_Click(object sender, RoutedEventArgs e)
    {
        await AboutRomajiDialog.ShowAsync();
    }

    private void DisplayMaintain_OnChecked(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<IDisplayKeepAwakeService>().RequestActive();
    }

    private void DisplayMaintain_OnUnchecked(object sender, RoutedEventArgs e)
    {
        Ioc.Default.GetRequiredService<IDisplayKeepAwakeService>().RequestRelease();
    }

    private void LogoffLastFMAccount_Click(object sender, RoutedEventArgs e)
    {
        LastFM.LastFMSession = null;
    }

    private void LoginLastFMAccount_Click(object sender, RoutedEventArgs e)
    {
        _ = Launcher.LaunchUriAsync(new Uri(
            $"https://www.last.fm/api/auth?api_key={Ioc.Default.GetRequiredService<LastFMClient>().Options.ApiKey}&cb=hyplayer://link.last.fm"));
    }

    private void BtnClearTileCache_Click(object sender, RoutedEventArgs e)
    {
        _tileService.ClearAllTiles();
    }

    private async void OpenLyricEffectSettings_Click(object sender, RoutedEventArgs e)
    {
        await new LyricEffectSettingsDialog().ShowAsync();
    }

    [GeneratedBindableCustomProperty]
    public partial class FontInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    private async void OpenFocusedLyricEffectSettings_Click(object sender, RoutedEventArgs e)
    {
        await new HyPlayer.UI.Dialogs.FocusedLyricEffectSettingsDialog().ShowAsync();
    }
}
