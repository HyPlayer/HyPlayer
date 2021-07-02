using HyPlayer.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ThirdPartyLogin : Page
    {
        private bool IsQQ = false;
        private bool IsWB = false;
        private string DefaultUA;
        string codeSerial = "a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z,A,B,C,D,E,F,G,H,I,J,K,M,L,N,O,P,Q,R,S,T,U,V,W,X,Y,Z,1,2,3,4,5,6,7,8,9,0";
        private readonly string QQLoginUri = @"https://graph.qq.com/oauth2.0/show?which=Login&display=pc&client_id=100495085&response_type=code&redirect_uri=https://music.163.com/back/qq";
        private readonly string WXLoginUri = @"https://open.weixin.qq.com/connect/qrconnect?appid=wxe280063f5fb2528a&response_type=code&redirect_uri=https://music.163.com/back/weichat&scope=snsapi_login&state=&lang=zh_CN#wechat_redirect";
        private readonly string WBLoginUri = @"https://api.weibo.com/oauth2/authorize?client_id=301575942&response_type=code&redirect_uri=http://music.163.com/back/weibo&scope=friendships_groups_read,statuses_to_me_read,follow_app_official_microblog&state=";
        public ThirdPartyLogin()
        {
            this.InitializeComponent();
        }
        private string CreateRandomState(int codeLen, bool ContainNum)
        {

            int Length = 4;
            if (codeLen == 0)
            {
                codeLen = Length;
            }

            string[] arr = codeSerial.Split(',');

            string code = "";

            int randValue = -1;

            Random rand = new Random(unchecked((int)DateTime.Now.Ticks));

            for (int i = 0; i < codeLen; i++)
            {
                randValue = rand.Next(0, ContainNum ? arr.Length - 1 : arr.Length - 11);

                code += arr[randValue];
            }

            return code;

        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            UAHelper.SetUserAgent(DefaultUA);
            ThirdPartyLoginWebview = null;
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DefaultUA = UAHelper.GetUserAgent();
            UAHelper.SetUserAgent("Mozilla/5.0 (Windows Phone 10.0; Android 6.0.1; Microsoft; Lumia 950) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/52.0.2743.116 Mobile Safari/537.36 Edge/15.15063");
            switch (e.Parameter as String)
            {

                case "QQ":
                    IsQQ = true;
                    ThirdPartyLoginWebview.Navigate(new Uri("https://y.music.163.com/m/login"));
                    break;
                case "WX":
                    ThirdPartyLoginWebview.Navigate(new Uri(WXLoginUri + CreateRandomState(10, false)));
                    break;
                case "WB":
                    IsWB = true;
                    ThirdPartyLoginWebview.Navigate(new Uri("https://y.music.163.com/m/login"));
                    break;
            }
        }




        private async void ThirdPartyLoginWebview_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {

            if (sender.Source.ToString().StartsWith("https://xui.ptlogin2.qq.com"))
            {
                Regex match = new Regex("state=[a-zA-Z]{10}");
                if (match.IsMatch(sender.Source.ToString()))
                {
                    UAHelper.SetUserAgent(DefaultUA);
                    ThirdPartyLoginWebview.Navigate(new Uri(QQLoginUri + "&" + match.Match(sender.Source.ToString())));

                }

            }

            if (sender.Source.ToString().StartsWith("https://api.weibo.com"))
            {

                await ThirdPartyLoginWebview.InvokeScriptAsync("eval", new[]
{
    "document.getElementById('jump_login_url_a').click();"
});


            }

            if (sender.Source.ToString().StartsWith("https://y.music.163.com/m/login")&& !sender.Source.ToString().Contains("code=200"))
            {
                await ThirdPartyLoginWebview.InvokeScriptAsync("eval", new[]{
                    "var x = document.getElementsByClassName('u-terms-checkbox');" +
                    "x[0].click();"
});
                if (IsQQ)
                {
                    await ThirdPartyLoginWebview.InvokeScriptAsync("eval", new[]
{
    "var x = document.getElementsByClassName('item');" +
    "x[0].click();"
});
                }
                else if (IsWB)
                {
                    await ThirdPartyLoginWebview.InvokeScriptAsync("eval", new[]
{
    "var x = document.getElementsByClassName('item');" +
    "x[1].click();"
});
                   
                }
            }

            if (sender.Source.ToString().StartsWith("https://music.163.com")|| sender.Source.ToString().Contains("code=200"))
            {

                var cookies = new Windows.Web.Http.Filters.HttpBaseProtocolFilter().CookieManager.GetCookies(new Uri("https://music.163.com"));
                string cookiestring = string.Empty;
                foreach (Windows.Web.Http.HttpCookie cookie in cookies)
                {
                    System.Net.Cookie rescookie = new System.Net.Cookie();
                    rescookie.Name = cookie.Name;
                    rescookie.Value = cookie.Value;
                    rescookie.HttpOnly = cookie.HttpOnly;
                    rescookie.Domain = cookie.Domain;
                    rescookie.Secure = cookie.Secure;

                    rescookie.Expires = ((DateTimeOffset)cookie.Expires).DateTime;
                    rescookie.Path = cookie.Path;
                    Common.ncapi.Cookies.Add(rescookie);
                }
                Common.PageBase.LoginDone();
            }
        }
    }
}
