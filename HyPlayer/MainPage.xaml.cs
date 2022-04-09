#region

using System;
using System.Net;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.Pages;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MainPage
{
    public MainPage()
    {
        Common.PageMain = this;
        Common.ncapi.RealIP = Setting.GetSettings<string>("xRealIp", null);
        Common.ncapi.Proxy = new WebProxy(Setting.GetSettings<string>("neteaseProxy", null));
        Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
        StaticSource.PICSIZE_AUDIO_PLAYER_COVER = Common.Setting.highQualityCoverInSMTC ? "1024y1024" : "100y100";
        if (Common.Setting.uiSound)
        {
            ElementSoundPlayer.State = ElementSoundPlayerState.Off;
            ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
        }

        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        (GridPlayBar.Children[0] as PlayBar).RefreshSongList();
        switch (e.Parameter)
        {
            case "search":
                Common.NavigatePage(typeof(Search));
                break;
            case "account":
                Common.NavigatePage(typeof(Me));
                break;
            case "likedsongs":
                Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
                break;
            case "local":
                Common.NavigatePage(typeof(LocalMusicPage));
                break;
        }
    }
}

public class PlayBarMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? new Thickness(16) : new Thickness(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PlayBarCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? new CornerRadius(16) : new CornerRadius(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}