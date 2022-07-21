using System;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.System;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Utils;
using Newtonsoft.Json;
using System.Linq;
using TagLib.NonContainer;
using System.Collections.Generic;
using System.Threading.Tasks;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class TestPage : Page
{
    private int _teachingTipIndex;


    public string ResourceId
    {
        get { return (string)GetValue(ResourceIdProperty); }
        set { SetValue(ResourceIdProperty, value); }
    }

    public static readonly DependencyProperty ResourceIdProperty =
        DependencyProperty.Register("ResourceId", typeof(string), typeof(TestPage), new PropertyMetadata(""));


    public TestPage()
    {
        InitializeComponent();
    }

    private void TestTeachingTip_OnClick(object sender, RoutedEventArgs e)
    {
        Common.AddToTeachingTipLists("TestTeachingTip", _teachingTipIndex++.ToString());
    }

    private void NavigateResourceId(object sender, RoutedEventArgs e)
    {
        Common.NavigatePageResource(ResourceId);
    }

    private async void PlayResourceId(object sender, RoutedEventArgs e)
    {
        await HyPlayList.AppendNcSource(ResourceId);
        HyPlayList.SongAppendDone();
    }

    private async void DumpDebugInfo_Click(object sender, RoutedEventArgs e)
    {
        var info = JsonConvert.SerializeObject(new DumpInfo
        {
            CurrentSong = HyPlayList.NowPlayingItem,
            CurrentPlaySource = HyPlayList.PlaySourceId,
            CurrentUser = Common.LoginedUser,
            DeviceType = new DeviceInformationHelper().GetDeviceInformation(),
            DeviceId = new EasClientDeviceInformation().Id.ToString(),
            IsInBackground = Common.IsInBackground,
            IsUsingCache = Common.Setting.enableCache,
            IsLowCache = Common.Setting.forceMemoryGarbage,
            ErrorMessageList = Common.ErrorMessageList.TakeLast(15).ToList()
        }, Formatting.Indented);
        var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("dump-" +
            DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid() + ".txt");
        await FileIO.WriteTextAsync(file, info);
        Launcher.LaunchFileAsync(file);
    }

    private class DumpInfo
    {
        public HyPlayItem CurrentSong { get; set; }
        public string CurrentPlaySource { get; set; }
        public NCUser CurrentUser { get; set; }
        public Microsoft.AppCenter.Ingestion.Models.Device DeviceType { get; set; }
        public string DeviceId { get; set; }
        public bool IsInBackground { get; set; }
        public bool IsUsingCache { get; set; }
        public bool IsLowCache { get; set; }
        public List<string> ErrorMessageList { get; set; }
    }

    private void DisablePopUpButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.DisablePopUp = true;
    }
}