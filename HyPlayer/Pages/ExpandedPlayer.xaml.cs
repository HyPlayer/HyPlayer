using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ExpandedPlayer : Page
    {
        private Timer timer;
        public ExpandedPlayer()
        {
            this.InitializeComponent();
            Common.PageExpandedPlayer = this;
            
            timer = new Timer((state =>
            {
                this.Invoke((() =>
                {

                }));
            }), null, 0, 100);
            
        }

        public void LoadLyricsBox()
        {
            LyricBox.Children.Clear();
            if (AudioPlayer.Lyrics.Count == 0)
            {
                LyricItem lrcitem = new LyricItem(SongLyric.PureSong);
                LyricBox.Children.Add(lrcitem);
            }
            else
            {
                foreach (SongLyric songLyric in AudioPlayer.Lyrics)
                {
                    LyricItem lrcitem = new LyricItem(songLyric);
                    lrcitem.Margin = new Thickness(0, 10, 0, 10);
                    LyricBox.Children.Add(lrcitem);
                }
            }
        }

        public void OnSongChange(MediaPlaybackItem mpi)
        {
            if (mpi != null)
            {
                this.Invoke((() =>
                {
                    ImageAlbum.Source = AudioPlayer.AudioInfos[mpi]
                        .Picture;
                    TextBlockSinger.Text = AudioPlayer.AudioInfos[mpi]
                        .Artist;
                    TextBlockSongTitle.Text = AudioPlayer.AudioInfos[mpi]
                        .SongName;
                    this.Background = new ImageBrush() { ImageSource = ImageAlbum.Source };
                    LoadLyricsBox();
                }));
            }
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        public void StartExpandAnimation()
        {
            var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
            var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
            var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
            anim3.Configuration = new DirectConnectedAnimationConfiguration();
            anim2.Configuration = new DirectConnectedAnimationConfiguration();
            anim1.Configuration = new DirectConnectedAnimationConfiguration();
            anim3?.TryStart(TextBlockSinger);
            anim1?.TryStart(TextBlockSongTitle);
            anim2?.TryStart(ImageAlbum);
        }

        public void StartCollapseAnimation()
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TextBlockSongTitle);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", ImageAlbum);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TextBlockSinger);
        }

    }
}
