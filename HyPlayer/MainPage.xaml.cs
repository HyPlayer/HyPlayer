using System;
using System.Net;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using Kawazu;
using HyPlayer.Controls;
using HyPlayer.Classes;
using Windows.UI.Xaml;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace HyPlayer
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            Common.PageMain = this;
            Common.ncapi.RealIP = Setting.GetSettings<string>("xRealIp", null);
            Common.ncapi.Proxy = new WebProxy(Setting.GetSettings<string>("neteaseProxy", null));
            Common.ncapi.UseProxy = !(ApplicationData.Current.LocalSettings.Values["neteaseProxy"] is null);
            StaticSource.PICSIZE_AUDIO_PLAYER_COVER = Common.Setting.highQualityCoverInSMTC ? "1024y1024" : "100y100";
            if (Common.Setting.uiSound)
            {
                ElementSoundPlayer.State = ElementSoundPlayerState.Off;
                ElementSoundPlayer.SpatialAudioMode = ElementSpatialAudioMode.Off;
            }
            NavigationCacheMode = NavigationCacheMode.Required;
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            (GridPlayBar.Children[0] as PlayBar).RefreshSongList();
            switch (e.Parameter)
            {
                case "search":
                    Common.NavigatePage(typeof(Search));
                    break;
                case "account":
                    Common.NavigatePage(typeof(Me));
                    break;
                case "likedsongs":
                    Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
                    break;
                case "local":
                    Common.NavigatePage(typeof(LocalMusicPage));
                    break;
            }
        }

        private void Page_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (Common.PageExpandedPlayer != null)
                Common.PageExpandedPlayer.ExpandedPlayer_OnPointerEntered(sender, e);
        }

        private void Page_PointerExited(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (Common.PageExpandedPlayer != null)
                Common.PageExpandedPlayer.ExpandedPlayer_OnPointerExited(sender, e);
        }
    }
}