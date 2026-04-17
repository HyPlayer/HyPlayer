using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;


// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class TestPage : Page
{
    public static readonly DependencyProperty ResourceIdProperty =
        DependencyProperty.Register(nameof(ResourceId), typeof(string), typeof(TestPage), new PropertyMetadata(""));

    private int _teachingTipIndex;

    static readonly List<KeyValuePair<string, WeakReference<FrameworkElement>>> controlsReferences = [];

    static readonly Dictionary<Type, object> typeParams = new()
    {
        [typeof(AlbumPage)] = "97767168",
        [typeof(ArtistPage)] = "159692",
        [typeof(Comments)] = "sg211277",
        // [typeof(CompactPlayerPage)] = null, // need new app window
        [typeof(DownloadPage)] = null,
        // [typeof(ExpandedPlayer)] = null,
        [typeof(History)] = null,
        [typeof(HomePage)] = null,
        [typeof(LocalMusicPage)] = null,
        [typeof(Me)] = null,
        [typeof(MusicCloudPage)] = null,
        [typeof(MVPage)] = "14417823",
        [typeof(PageFavorite)] = null,
        [typeof(RadioPage)] = "793914432",
        [typeof(Search)] = "初音未来",
        [typeof(Settings)] = null,
        [typeof(SongListDetail)] = "897784673",
        [typeof(Welcome)] = null,
        [typeof(BlankPage)] = null,
    };

    public TestPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        TbAdditionalApiParameters.Text = JsonSerializer.Serialize(Common.Setting.ApiAdditionalParameters, Common.DefaultOptions);
    }

    public string ResourceId
    {
        get => (string)GetValue(ResourceIdProperty);
        set => SetValue(ResourceIdProperty, value);
    }

    private void TestTeachingTip_OnClick(object sender, RoutedEventArgs e)
    {
        Common.AddToTeachingTipLists("TestTeachingTip", _teachingTipIndex++.ToString());
    }

    private async void TestGCLeak_Click(object sender, RoutedEventArgs e)
    {
        var leakCheckFrame = new Frame
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 500
        };
        MainStackPanel.Children.Insert(0, leakCheckFrame);
        leakCheckFrame.Visibility = Visibility.Visible;
        foreach (var (type, param) in typeParams)
        {
            leakCheckFrame.Navigate(type, param);
            await Task.Delay(500);
            var page = leakCheckFrame.Content as Page;
            controlsReferences.Add(new KeyValuePair<string, WeakReference<FrameworkElement>>(type.Name, new WeakReference<FrameworkElement>(page as FrameworkElement)));
            GC.Collect();
            await Task.Delay(5000);
        }
        MainStackPanel.Children.Remove(leakCheckFrame);
        Common.AddToTeachingTipLists("正在生成报告", "等待 GC 处理中");
        GC.Collect();
        await Task.Delay(5000);
        GC.Collect();
        var resultSb = new StringBuilder();
        foreach (var (name, reference) in controlsReferences)
        {
            resultSb.AppendLine(name + ": " + (reference.TryGetTarget(out _) ? "Alive" : "Collected"));
        }
        var result = resultSb.ToString();
        var contentDialog = new ContentDialog
        {
            Title = "GC Leak Check Result",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = result,
                    FontSize = 14
                }
            },
            CloseButtonText = "OK"
        };
        _ = contentDialog.ShowAsync();
    }

    private void NavigateResourceId(object sender, RoutedEventArgs e)
    {
        _ = Common.NavigatePageResource(ResourceId);
    }

    private async void PlayResourceId(object sender, RoutedEventArgs e)
    {
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        _playlist.Clear();
        await _playlist.AppendNcSourceAsync(ResourceId);
    }

    private async void DumpDebugInfo_Click(object sender, RoutedEventArgs e)
    {
        var _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        var info = JsonSerializer.Serialize(new DumpInfo
        {
            CurrentSong = _state.NowPlayingItem,
            CurrentPlaySource = _playlist.PlaySourceId,
            CurrentUser = Common.LoginedUser,
            DeviceId = new EasClientDeviceInformation().Id.ToString(),
            IsInBackground = Common.IsInBackground,
            IsLowCache = Common.Setting.forceMemoryGarbage,
            ErrorMessageList = [.. Common.ErrorMessageList.TakeLast(15)]
        }, Common.DefaultOptions);
        var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("dump-" +
            DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid() + ".txt");
        await FileIO.WriteTextAsync(file, info);
        _ = Launcher.LaunchFileAsync(file);
    }

    private void DisablePopUpButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.DisablePopUp = true;
    }

    private void ForceGC_Click(object sender, RoutedEventArgs e)
    {
        GC.Collect();
    }

    private void SaveApiAdditionalParameters_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = JsonSerializer.Deserialize<AdditionalParameters>(TbAdditionalApiParameters.Text, Common.DefaultOptions);
            if (result != null)
            {
                Common.Setting.ApiAdditionalParameters = result;
                Common.NeteaseAPI!.Option.AdditionalParameters = result;
                HyPlayList.LoginDoneCall();
                Common.AddToTeachingTipLists("成功设置API附加参数", "请重启应用以使更改生效");
            }
            else
            {
                throw new Exception("Invalid JSON");
            }
        }
        catch (Exception ex)
        {
            ContentDialog dialog = new()
            {
                Title = "Error",
                Content = ex.Message,
                CloseButtonText = "OK"
            };
            _ = dialog.ShowAsync();
        }
    }
}