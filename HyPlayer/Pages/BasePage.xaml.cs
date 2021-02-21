using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NavigationView = Windows.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Windows.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Windows.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class BasePage : Page
    {
        public BasePage()
        {
            this.InitializeComponent();
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            LoadLoginData();
            if (Common.Logined)
            {
                TextBlockUserName.Text = Common.LoginedUser.UserName;
                PersonPictureUser.ProfilePicture = new BitmapImage(new Uri(Common.LoginedUser.ImgUrl));
            }
            Common.BaseFrame = BaseFrame;
        }

        private async void LoadLoginData()
        {
            try
            {
                StorageFile sf = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFileAsync("Settings\\UserPassword");
                string txt = await FileIO.ReadTextAsync(sf);
                string[] arr = txt.Split("\r\n");
                var queries = new Dictionary<string, object>();
                var account = arr[0];
                var isPhone = Regex.Match(account, "^[0-9]+$").Success;
                queries[isPhone ? "phone" : "email"] = account;
                queries["md5_password"] = arr[1];
                var (isOk, json) = await Common.ncapi.RequestAsync(isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
                if (isOk && json["code"].ToString() == "200")
                {
                    Common.Logined = true;
                    Common.LoginedUser.UserName = json["profile"]["nickname"].ToString();
                    Common.LoginedUser.ImgUrl = json["profile"]["avatarUrl"].ToString();
                    Common.LoginedUser.uid = json["account"]["id"].ToString();
                    TextBlockUserName.Text = json["profile"]["nickname"].ToString();
                    PersonPictureUser.ProfilePicture =
                        new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                }

            }
            catch (Exception) { }

        }

        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs e)
        {
            ButtonLogin.IsEnabled = false;
            ButtonLogin.Content = "登录中......";
            bool isOk;
            JObject json;

            var queries = new Dictionary<string, object>();
            var account = TextBoxAccount.Text;
            var isPhone = Regex.Match(account, "^[0-9]+$").Success;
            queries[isPhone ? "phone" : "email"] = account;
            queries["password"] = TextBoxPassword.Password;
            (isOk, json) = await Common.ncapi.RequestAsync(isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
            if (!isOk || json["code"].ToString() != "200")
            {
                ButtonLogin.Visibility = Visibility.Visible;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Title = "登录失败";
                ButtonLogin.Content = "登录";
                ButtonLogin.IsEnabled = true;
                InfoBarLoginHint.Severity = InfoBarSeverity.Warning;
                InfoBarLoginHint.Message = "登录失败 " + json["msg"];
            }
            else
            {
                Common.Logined = true;
                StorageFile sf = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync("Settings\\UserPassword", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                _ = FileIO.WriteTextAsync(sf, account + "\r\n" + TextBoxPassword.Password.ToString().ToByteArrayUtf8().ComputeMd5().ToHexStringLower());
                Common.LoginedUser.UserName = json["profile"]["nickname"].ToString();
                Common.LoginedUser.ImgUrl = json["profile"]["avatarUrl"].ToString();
                Common.LoginedUser.uid = json["account"]["id"].ToString();
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Title = "登录成功";
                ButtonLogin.Content = "登录成功";
                TextBlockUserName.Text = json["profile"]["nickname"].ToString();
                PersonPictureUser.ProfilePicture = new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                InfoBarLoginHint.Severity = InfoBarSeverity.Success;
                InfoBarLoginHint.Message = "欢迎 " + json["profile"]["nickname"].ToString();
            }
        }

        private void InfoBarLoginHint_OnCloseButtonClick(InfoBar sender, object args)
        {
            if (Common.Logined) DialogLogin.Hide();
        }

        private async void NavMain_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            NavigationViewItem nowitem = (NavigationViewItem)sender.SelectedItem;
            if (nowitem.Tag.ToString() == "PageMe" && !Common.Logined)
            {
                await DialogLogin.ShowAsync();
                return;
            }

            switch (nowitem.Tag.ToString())
            {
                case "PageMe":
                    Common.BaseFrame.Navigate(typeof(Pages.Me), null, new EntranceNavigationTransitionInfo());
                    break;
            }
        }
    }
}
