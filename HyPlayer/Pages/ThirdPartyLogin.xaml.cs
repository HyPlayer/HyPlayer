#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

using HyPlayer.Services.Abstractions;
#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ThirdPartyLogin : Page
{
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();

    private string LoginType = "5";

    public bool Navigated;

    public ThirdPartyLogin()
    {
        InitializeComponent();
    }


    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ThirdPartyLoginWebview.Close();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        switch (e.Parameter as string)
        {
            case "QQ":
                LoginType = "5";
                break;
            case "WX":
                LoginType = "10";
                break;
            case "WB":
                LoginType = "2";
                break;
        }

        Navigated = false;
    }
    private void ThirdPartyLoginWebview_Loaded(object sender, RoutedEventArgs e)
    {
        // 10 - 微信    5 - QQ     2 - 微博
        if (!Navigated)
        {
            ThirdPartyLoginWebview.Source = new Uri("http://music.163.com/api/sns/authorize?snsType=" + LoginType + "&clientType=mobile&callbackType=Login");
            Navigated = true;
            LoadingRingContainer.Visibility = Visibility.Collapsed;
        }
    }

    private async void ThirdPartyLoginWebview_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (sender.Source.ToString().StartsWith("https://music.163.com/back/sns"))
        {
            LoadingRingContainer.Visibility = Visibility.Visible;
            var cookies = await sender.CoreWebView2.CookieManager.GetCookiesAsync("https://music.163.com");
            foreach (var cookie in cookies)
            {
                _api.Option.Cookies.Add(cookie.Name, cookie.Value);
            }

            await SimpleCacher.ClearCacheAsync(CacheType.Login);
            await (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage)!.LoginDone();
        }
    }
}