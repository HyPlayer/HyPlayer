using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using NavigationView = Windows.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Windows.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Windows.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }


        private void NavMain_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            NavigationViewItem nowitem = (NavigationViewItem) sender.SelectedItem;
            if (nowitem.Tag.ToString() == "PageMe" && !Common.Logined)
            {
                DialogLogin.ShowAsync();
            }
        }

        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs e)
        {

            bool isOk;
            JObject json;
            Dictionary<string, string> queries;
            string account;
            bool isPhone;

            queries = new Dictionary<string, string>();
            account = TextBoxAccount.Text;
            isPhone = Regex.Match(account, "^[0-9]+$").Success;
            queries[isPhone ? "phone" : "email"] = account;
            queries["password"] = TextBoxPassword.Password;
            (isOk, json) = await Common.ncapi.RequestAsync(isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
            if (!isOk || json["code"].ToString() != "200")
            {
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Title = "登录失败";
                InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                InfoBarLoginHint.Message = "登录失败 " + json["msg"];
            }
            else
            {
                Common.Logined = true;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Title = "登录成功";
                InfoBarLoginHint.Severity = InfoBarSeverity.Success;
                InfoBarLoginHint.Message = "欢迎 "+json["profile"]["nickname"];
            }
        }
    }
}
