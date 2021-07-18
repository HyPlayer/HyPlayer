using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HttpMethod = Windows.Web.Http.HttpMethod;
using HttpRequestMessage = Windows.Web.Http.HttpRequestMessage;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ThirdPartyLogin : Page
    {
        string LoginType = "5";

        public ThirdPartyLogin()
        {
            this.InitializeComponent();
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

            switch (e.Parameter as String)
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
                default:
                    break;
            }

            Navigated = false;
            ThirdPartyLoginWebview.Navigate(new Uri("https://music.163.com"));
        }


        private void ThirdPartyLoginWebview_NavigationCompleted(WebView sender,
            WebViewNavigationCompletedEventArgs args)
        {
            if (sender.Source.ToString().StartsWith("https://music.163.com/back/sns"))
            {
                var cookies =
                    new Windows.Web.Http.Filters.HttpBaseProtocolFilter().CookieManager.GetCookies(
                        new Uri("https://music.163.com"));
                string cookiestring = string.Empty;
                foreach (Windows.Web.Http.HttpCookie cookie in cookies)
                {
                    System.Net.Cookie rescookie = new System.Net.Cookie();
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

                Common.PageBase.LoginDone();
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
            }
        }

        public bool Navigated;
    }
}