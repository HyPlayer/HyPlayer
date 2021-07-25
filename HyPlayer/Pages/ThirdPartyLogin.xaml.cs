using System;
using System.Net;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http.Filters;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ThirdPartyLogin : Page
    {
        private string LoginType = "5";

        public bool Navigated;

        public ThirdPartyLogin()
        {
            InitializeComponent();
        }


        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            await WebView.ClearTemporaryWebDataAsync();
            ThirdPartyLoginWebview = null;
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
            ThirdPartyLoginWebview.Navigate(new Uri("https://music.163.com"));
        }


        private async void ThirdPartyLoginWebview_NavigationCompleted(WebView sender,
            WebViewNavigationCompletedEventArgs args)
        {
            if (sender.Source.ToString().StartsWith("https://music.163.com/back/sns"))
            {
                LoadingRingContainer.Visibility = Windows.UI.Xaml.Visibility.Visible;
                var cookies =
                    new HttpBaseProtocolFilter().CookieManager.GetCookies(
                        new Uri("https://music.163.com"));
                var cookiestring = string.Empty;
                foreach (var cookie in cookies)
                {
                    var rescookie = new Cookie();
                    rescookie.Name = cookie.Name;
                    rescookie.Value = cookie.Value;
                    rescookie.HttpOnly = cookie.HttpOnly;
                    rescookie.Domain = cookie.Domain;
                    rescookie.Secure = cookie.Secure;
                    if (cookie.Expires != null)
                        rescookie.Expires = ((DateTimeOffset) cookie.Expires).DateTime;
                    rescookie.Path = cookie.Path;
                    Common.ncapi.Cookies.Add(rescookie);
                }

                await Common.PageBase.LoginDone();
            }
        }

        private void ThirdPartyLoginWebview_OnLoadCompleted(object sender, NavigationEventArgs e)
        {
            // 10 - 微信    5 - QQ     2 - 微博
            if (!Navigated)
            {
                ThirdPartyLoginWebview.Navigate(new Uri("http://music.163.com/api/sns/authorize?snsType=" + LoginType +
                                                        "&clientType=mobile&callbackType=Login"));
                Navigated = true;
                LoadingRingContainer.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }
        }
    }
}