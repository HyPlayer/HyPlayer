#region

using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Me : Page, IDisposable
    {
        private string uid = "";
        ObservableCollection<SimpleListItem> myPlayList = new ObservableCollection<SimpleListItem>();
        ObservableCollection<SimpleListItem> likedPlayList = new ObservableCollection<SimpleListItem>();

        public Me()
        {
            InitializeComponent();
        }

        public void Dispose()
        {
            ImageRect.ImageSource = null;
            myPlayList.Clear();
            likedPlayList.Clear();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Dispose();
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


                    int myListIdx = 0;
                    int subListIdx = 0;
                    foreach (var PlaylistItemJson in json["playlist"].ToArray())
                    {
                        var ncp = NCPlayList.CreateFromJson(PlaylistItemJson);
                        if (PlaylistItemJson["subscribed"].ToString() == "True")
                            //GridContainerSub.Children.Add(new PlaylistItem(ncp));
                            likedPlayList.Add(
                                new SimpleListItem
                                {
                                    CoverUri = ncp.cover + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                                    LineOne = ncp.creater.name,
                                    LineThree = null,
                                    LineTwo = null,
                                    Order = myListIdx++,
                                    ResourceId = "pl" + ncp.plid,
                                    Title = ncp.name
                                }
                            );
                        else
                            myPlayList.Add(
                                new SimpleListItem
                                {
                                    CoverUri = ncp.cover + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                                    LineOne = ncp.creater.name,
                                    LineThree = null,
                                    LineTwo = null,
                                    Order = subListIdx++,
                                    ResourceId = "pl" + ncp.plid,
                                    Title = ncp.name
                                }
                            );
                    }
                }
                catch (Exception ex)
                {
                    Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
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
                        Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
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
                ((App)Application.Current).InitializeJumpList();
            }
            catch
            {
            }
        }
    }
}