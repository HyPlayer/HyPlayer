using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Me : Page
    {
        private string uid = "";

        public Me()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter != null)
            {
                uid = (string) e.Parameter;
                ButtonLogout.Visibility = Visibility.Collapsed;
            }
            else
            {
                uid = Common.LoginedUser.id;
            }

            LoadInfo();
            LoadPlayList();
        }

        public async void LoadPlayList()
        {
            try
            {
                var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                    new Dictionary<string, object> {["uid"] = uid});

                if (isok)
                    foreach (var PlaylistItemJson in json["playlist"].ToArray())
                    {
                        var ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                        GridContainer.Children.Add(new PlaylistItem(ncp));
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public async void LoadInfo()
        {
            await Task.Run(() =>
            {
                Common.Invoke(async () =>
                {
                    var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserDetail,
                        new Dictionary<string, object> {["uid"] = uid});
                    if (isok)
                    {
                        TextBoxUserName.Text = json["profile"]["nickname"].ToString();
                        TextBoxSignature.Text = json["profile"]["signature"].ToString();
                        ImageRect.ImageSource = new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                    }
                });
            });
            /*
            await Task.Run(() =>
            {
                this.Invoke(async () =>
                {
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserLevel);
                    if (isok)
                    {
                        TextBlockLevel.Text = "LV. "+json["data"]["level"].ToString();
                    }
                });
            });*/
        }

        private async void Logout_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await Common.ncapi.RequestAsync(CloudMusicApiProviders.Logout);
                Common.Logined = false;
                Common.LoginedUser = new NCUser();
                ApplicationData.Current.LocalSettings.Values["cookie"] = "";
                Common.ncapi = new CloudMusicApi();
                Common.PageMain.MainFrame.Navigate(typeof(BlankPage));
                Common.PageMain.MainFrame.Navigate(typeof(BasePage));
                ((App)App.Current).InitializeJumpList();
            }
            catch
            {
            }
        }
    }
}