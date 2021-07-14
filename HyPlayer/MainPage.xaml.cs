using System;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml.Controls;
using Kawazu;
using Windows.UI.Xaml.Navigation;

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
            Common.PageMain = this;
            HyPlayList.InitializeHyPlaylist();
            HistoryManagement.InitializeHistoryTrack();
            Common.ncapi.RealIP = (string)ApplicationData.Current.LocalSettings.Values["xRealIp"];
            Common.ncapi.Proxy = new System.Net.WebProxy((string)ApplicationData.Current.LocalSettings.Values["neteaseProxy"]);
            Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
            Task.Run(() =>
            {
                Common.Invoke((async () =>
                {
                    try
                    {
                        var sf = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync("Romaji");
                        Common.KawazuConv = new KawazuConverter(sf.Path);
                    }
                    catch { }
                }));
            });
            InitializeComponent();
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            MainFrame.Navigate(typeof(Pages.BasePage));
            switch (e.Parameter)
            {
                case "search":
                    Common.BaseFrame.Navigate(typeof(Pages.Search));
                    break;
                case "account":
                    Common.BaseFrame.Navigate(typeof(Pages.Me));
                    break;
                case "local":
                    Common.BaseFrame.Navigate(typeof(Pages.LocalMusicPage));
                    break;
            }
        }
    }
}
