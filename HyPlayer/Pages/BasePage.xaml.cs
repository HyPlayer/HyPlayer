using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using HyPlayer.Classes;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;
using System.Linq;
using System.Threading.Tasks;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class BasePage : Page
    {
        private List<NavigationViewItem> selectionHistory;
        private bool IsNavBack;
        public BasePage()
        {
            InitializeComponent();
            selectionHistory = new List<NavigationViewItem>();
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationViewTitleBar titleBar = ApplicationView.GetForCurrentView().TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            LoadLoginData();
            if (Common.Logined)
            {
                TextBlockUserName.Text = Common.LoginedUser.name;
                PersonPictureUser.ProfilePicture = new BitmapImage(new Uri(Common.LoginedUser.avatar));
            }
            Common.BaseFrame = BaseFrame;
            NavMain.SelectedItem = NavMain.MenuItems[0];
            Common.BaseFrame.Navigate(typeof(Home));
        }

        private async void LoadLoginData()
        {
            try
            {
                StorageFile sf = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFileAsync("Settings\\UserPassword");
                string txt = await FileIO.ReadTextAsync(sf);
                string[] arr = txt.Split("\r\n");
                Dictionary<string, object> queries = new Dictionary<string, object>();
                string account = arr[0];
                bool isPhone = Regex.Match(account, "^[0-9]+$").Success;
                queries[isPhone ? "phone" : "email"] = account;
                queries["md5_password"] = arr[1];
                (bool isOk, JObject json) = await Common.ncapi.RequestAsync(isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
                if (isOk && json["code"].ToString() == "200")
                {
                    Common.Logined = true;
                    Common.LoginedUser.name = json["profile"]["nickname"].ToString();
                    Common.LoginedUser.avatar = json["profile"]["avatarUrl"].ToString();
                    Common.LoginedUser.id = json["account"]["id"].ToString();
                    Common.LoginedUser.signature = json["profile"]["signature"].ToString();
                    TextBlockUserName.Text = json["profile"]["nickname"].ToString();
                    PersonPictureUser.ProfilePicture =
                        new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                    LoginDone();
                }
            }
            catch
            {
                // ignored
            }
        }

        private async void ButtonLogin_OnClick(object sender, RoutedEventArgs e)
        {
            if(String.IsNullOrWhiteSpace(TextBoxAccount.Text)|| String.IsNullOrWhiteSpace(TextBoxPassword.Password))
            {
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Message = "用户名或密码不能为空";
                return;
            }
            ButtonLogin.IsEnabled = false;
            ButtonLogin.Content = "登录中......";
            bool isOk;
            JObject json;
            try
            {
                Dictionary<string, object> queries = new Dictionary<string, object>();
                string account = TextBoxAccount.Text;
                bool isPhone = Regex.Match(account, "^[0-9]+$").Success;
                queries[isPhone ? "phone" : "email"] = account;
                queries["password"] = TextBoxPassword.Password;
                (isOk, json) = await Common.ncapi.RequestAsync(
                    isPhone ? CloudMusicApiProviders.LoginCellphone : CloudMusicApiProviders.Login, queries);
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
                    StorageFile sf = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync(
                        "Settings\\UserPassword", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    _ = FileIO.WriteTextAsync(sf,
                        account + "\r\n" + TextBoxPassword.Password.ToString().ToByteArrayUtf8().ComputeMd5()
                            .ToHexStringLower());
                    Common.LoginedUser.name = json["profile"]["nickname"].ToString();
                    Common.LoginedUser.avatar = json["profile"]["avatarUrl"].ToString();
                    Common.LoginedUser.id = json["account"]["id"].ToString();
                    Common.LoginedUser.signature = json["profile"]["signature"].ToString();
                    InfoBarLoginHint.IsOpen = true;
                    InfoBarLoginHint.Title = "登录成功";
                    ButtonLogin.Content = "登录成功";
                    TextBlockUserName.Text = json["profile"]["nickname"].ToString();
                    PersonPictureUser.ProfilePicture =
                        new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                    InfoBarLoginHint.Severity = InfoBarSeverity.Success;
                    InfoBarLoginHint.Message = "欢迎 " + json["profile"]["nickname"].ToString();
                    LoginDone();
                }
            }
            catch (Exception ex)
            {
                ButtonLogin.IsEnabled = true;
                InfoBarLoginHint.IsOpen = true;
                InfoBarLoginHint.Severity = InfoBarSeverity.Error;
                StorageFile sf = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFileAsync(
                    "Settings\\Log", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                _ = FileIO.WriteTextAsync(sf,
                    ex.ToString());
                InfoBarLoginHint.Message = "登录失败 " + ex.ToString();
            }
        }

        private void ButtonCloseLoginForm_Click(object sender, RoutedEventArgs e)
        {
            DialogLogin.Hide();
        }


        private async void LoginDone()
        {
            //加载我喜欢的歌
            (bool isok, JObject js) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.Likelist, new Dictionary<string, object>() { { "uid", Common.LoginedUser.id } });
            Common.LikedSongs = js["ids"].ToObject<List<string>>();

            //加载用户歌单
            Microsoft.UI.Xaml.Controls.NavigationViewItem nowitem = NavItemsMyList;
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist, new Dictionary<string, object>() { { "uid", Common.LoginedUser.id } });
            if (isok)
            {
                NavItemsLikeList.Visibility = Visibility.Visible;
                NavItemsMyList.Visibility = Visibility.Visible;
                foreach (JToken jToken in json["playlist"])
                {
                    if (jToken["subscribed"].ToString() == "True")
                    {
                        NavItemsLikeList.MenuItems.Add(new NavigationViewItem()
                        {
                            Content = jToken["name"].ToString(),
                            Tag = "Playlist" + jToken["id"]
                        });
                    }
                    else
                    {
                        NavItemsMyList.MenuItems.Add(new NavigationViewItem()
                        {
                            Content = jToken["name"].ToString(),
                            Tag = "Playlist" + jToken["id"]
                        });
                    }
                }
            }
            DialogLogin.Hide();
        }



        private async void NavMain_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var nowitem = sender.SelectedItem as NavigationViewItem;
            if (!IsNavBack)
                selectionHistory.Add(nowitem);
            if (selectionHistory.Count > 1)
                NavMain.IsBackEnabled = true;
            IsNavBack = false;
            if (nowitem.Tag is null) return;
            if (nowitem.Tag.ToString() == "PersonalFM")
            {
                PersonalFM.InitPersonalFM();
                return;
            }

            if (nowitem.Tag.ToString() == "PageMe" && !Common.Logined)
            {
                InfoBarLoginHint.IsOpen = true;
                await DialogLogin.ShowAsync();
                return;
            }

            if (nowitem.Tag.ToString().StartsWith("Playlist"))
            {
                Common.BaseFrame.Navigate(typeof(Pages.SongListDetail), nowitem.Tag.ToString().Substring(8), new EntranceNavigationTransitionInfo());
            }

            switch (nowitem.Tag.ToString())
            {
                case "PageMe":
                    Common.BaseFrame.Navigate(typeof(Pages.Me), null, new EntranceNavigationTransitionInfo());
                    break;

                case "PageHome":
                    Common.BaseFrame.Navigate(typeof(Pages.Home), null, new EntranceNavigationTransitionInfo());
                    break;
                case "PageSettings":
                    Common.BaseFrame.Navigate(typeof(Pages.Settings), null, new EntranceNavigationTransitionInfo());
                    break;
            }
        }

        private void OnNavigateBack(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (Common.BaseFrame.CanGoBack)
            {
                Common.BaseFrame.GoBack();
            }
            selectionHistory.RemoveAt(selectionHistory.Count - 1);
            NavMain.SelectedItem = selectionHistory.Last();
            if (selectionHistory.Count <= 1)
                NavMain.IsBackEnabled = false;
        }

        private void TextBoxAccount_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                TextBoxPassword.Focus(FocusState.Keyboard);
            }
        }

        private void TextBoxPassword_OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                ButtonLogin_OnClick(null, null);
            }
        }

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            BaseFrame.Navigate(typeof(Search), args.QueryText);
        }

        private async void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (string.IsNullOrEmpty(sender.Text))
            {
                AutoSuggestBox_GotFocus(sender, null);
                return;
            }
            (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchSuggest, new Dictionary<string, object>() { { "keywords", sender.Text },{ "type","mobile" } });

            if (isOk && json["result"]["allMatch"].HasValues)
            {
                sender.ItemsSource = json["result"]["allMatch"].ToArray().ToList().Select(t => t["keyword"].ToString()).ToList();
            }
        }

        private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            sender.Text = args.SelectedItem.ToString();
        }


        private async void AutoSuggestBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace((sender as AutoSuggestBox)?.Text))
            {
                (bool isOk, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.SearchHot);
                if (isOk)
                {
                    ((AutoSuggestBox) sender).ItemsSource = json["result"]["hots"].ToArray().ToList().Select(t=>t["first"].ToString());
                }
            }
        }
    }
}