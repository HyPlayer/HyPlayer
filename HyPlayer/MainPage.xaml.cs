using System;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml.Controls;
using Kawazu;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

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
            bool IsFDOn = Common.Setting.FDOption;
            if (IsFDOn)
                this.Background = Application.Current.Resources["SystemControlAcrylicWindowBrush"] as Brush;
            else this.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
        }
    }
}