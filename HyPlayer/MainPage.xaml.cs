using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
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
            this.InitializeComponent();
            this.Opacity = 10;
            ButtonBase_OnClick(null,null);
        }

        private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            StorageFile sf = await StorageFile.GetFileFromPathAsync(
                @"D:\CloudMusic\无职转生\大原ゆい子 - 旅人の唄 (Hi-res).flac");
            MediaSource ms = MediaSource.CreateFromStorageFile(sf);
            var _mediaPlaybackItem = new MediaPlaybackItem(ms);
            var properties = _mediaPlaybackItem.GetDisplayProperties();
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = "旅人の唄";
            properties.MusicProperties.Artist = "大原ゆい子";
            properties.MusicProperties.Title = "旅人の唄";
            _mediaPlaybackItem.ApplyDisplayProperties(properties);
            AudioGraphSettings settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);

            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);
            MediaPlayer mp = new MediaPlayer()
            {
                Source = _mediaPlaybackItem
            };
            mp.Play();
        }
    }
}
