using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using Microsoft.UI.Xaml.Controls;
using NeteaseCloudMusicApi;
using Newtonsoft.Json.Linq;
using NavigationView = Windows.UI.Xaml.Controls.NavigationView;
using NavigationViewItem = Windows.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = Windows.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

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
            Common.MainFrame = MainFrame;
            Common.PageMain = this;
            AudioPlayer.AudioMediaPlaybackList = new MediaPlaybackList();
            AudioPlayer.AudioMediaPlaybackList.ItemOpened += AudioPlayer.AudioMediaPlaybackList_ItemOpened;
            AudioPlayer.AudioMediaPlaybackList.CurrentItemChanged += AudioPlayer.AudioMediaPlaybackList_CurrentItemChanged;
            AudioPlayer.AudioMediaPlayer = new MediaPlayer()
            {
                Source = AudioPlayer.AudioMediaPlaybackList,
            };
            this.InitializeComponent();
        }
    }
}
