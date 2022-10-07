using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using Microsoft.AppCenter.Ingestion.Models;
using Microsoft.AppCenter.Utils;
using Newtonsoft.Json;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class TestPage : Page
{
    public static readonly DependencyProperty ResourceIdProperty =
        DependencyProperty.Register("ResourceId", typeof(string), typeof(TestPage), new PropertyMetadata(""));

    private int _teachingTipIndex;


    public TestPage()
    {
        InitializeComponent();
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

    private void NavigateResourceId(object sender, RoutedEventArgs e)
    {
        _ = Common.NavigatePageResource(ResourceId);
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
        _ = Launcher.LaunchFileAsync(file);
    }

    private void DisablePopUpButton_Click(object sender, RoutedEventArgs e)
    {
        Common.Setting.DisablePopUp = true;
    }

    private class DumpInfo
    {
        public HyPlayItem CurrentSong { get; set; }
        public string CurrentPlaySource { get; set; }
        public NCUser CurrentUser { get; set; }
        public Device DeviceType { get; set; }
        public string DeviceId { get; set; }
        public bool IsInBackground { get; set; }
        public bool IsUsingCache { get; set; }
        public bool IsLowCache { get; set; }
        public List<string> ErrorMessageList { get; set; }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
            var response = await LastFMManager.TryLoginLastfmAccountFromInternet(UserName.Text, Password.Text);
            if (response.Success)
            {
                Common.AddToTeachingTipLists("LastFM Logined");
            }
            else
            {
                Common.AddToTeachingTipLists("LastFM Login Failed", response.Status.ToString());
            }
    }

    private async void Scrobble_Click(object sender, RoutedEventArgs e)
    {
        var response = await LastFMManager.ScrobbleAsync(HyPlayList.NowPlayingItem);
        if (response)
        {
            Common.AddToTeachingTipLists("LastFM Scrobbled");
        }
    }

    private async void UpdateNowPlaying_Click(object sender, RoutedEventArgs e)
    {
        var response = await LastFMManager.UpdateNowPlayingAsync(HyPlayList.NowPlayingItem);
        if (response)
        {
            Common.AddToTeachingTipLists("LastFM NowPlayingItem Updated");
        }
    }
}