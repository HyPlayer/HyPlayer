using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class Me : Page
    {
        private string uid = "";
        public Me()
        {
            this.InitializeComponent();
            uid = Common.LoginedUser.uid;
            LoadInfo();
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            uid = Common.LoginedUser.uid;
            LoadInfo();
        }

        public async void LoadInfo()
        {
            
            await Task.Run(( () =>
            {
                this.Invoke(async () =>
                {
                    (bool isok, JObject json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserAccount);
                    if (isok)
                    {
                        TextBoxUserName.Text = json["profile"]["nickname"].ToString();
                        TextBoxSignature.Text = json["profile"]["signature"].ToString();
                        ImageRect.ImageSource = new BitmapImage(new Uri(json["profile"]["avatarUrl"].ToString()));
                    }
                });

            }));
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

        private void NavigationView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            switch (args.SelectedItemContainer.Tag.ToString())
            {
                case "SongList":
                    Common.GLOBAL["SongListUID"] = uid;
                    ContentFrame.Navigate(typeof(SongListFrame), null,new EntranceNavigationTransitionInfo());
                    break;
            }
        }
    }
}
