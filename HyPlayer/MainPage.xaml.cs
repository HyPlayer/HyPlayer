using System;
using System.Threading.Tasks;
using Windows.Storage;
using HyPlayer.HyPlayControl;
using Windows.UI.Xaml.Controls;
using Kawazu;

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
    }
}
