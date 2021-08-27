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
    public sealed partial class Me : Page, IDisposable
    {
        private string uid = "";

        public Me()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            GridContainerMy.Children.Clear();
            GridContainerSub.Children.Clear();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter != null)
            {
                uid = (string)e.Parameter;
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
                try
                {
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserPlaylist,
                        new Dictionary<string, object> { ["uid"] = uid });


                    foreach (var PlaylistItemJson in json["playlist"].ToArray())
                    {
                        var ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                        if (PlaylistItemJson["subscribed"].ToString() == "True")
                            GridContainerSub.Children.Add(new PlaylistItem(ncp));
                        else
                            GridContainerMy.Children.Add(new PlaylistItem(ncp));
                    }
                }
                catch (Exception ex)
                {
                    Common.ShowTeachingTip("发生错误", ex.Message);
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
                    try
                    {
                        var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserDetail,
                            new Dictionary<string, object> { ["uid"] = uid });

                        TextBoxUserName.Text = json["profile"]["nickname"].ToString();
                        TextBoxSignature.Text = json["profile"]["signature"].ToString();
                        ImageRect.ImageSource = new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                    }
                    catch (Exception ex)
                    {
                        Common.ShowTeachingTip("发生错误", ex.Message);
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

        public void Dispose()
        {
            ImageRect.ImageSource = null;
            GridContainerMy.Children.Clear();
            GridContainerSub.Children.Clear();
        }
    }
}