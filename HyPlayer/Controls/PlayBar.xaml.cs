using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
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
using HyPlayer.Pages;
using Microsoft.Toolkit.Extensions;
using Microsoft.UI.Xaml.Media;
using TagLib;
using AcrylicBackgroundSource = Windows.UI.Xaml.Media.AcrylicBackgroundSource;
using File = TagLib.File;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{

    public sealed partial class PlayBar : UserControl
    {
        public PlayBar()
        {
            Common.BarPlayBar = this;
            this.InitializeComponent();
            //TestFile();
        }


        private async void TestFile()
        {
            FileOpenPicker fop = new FileOpenPicker();
            fop.FileTypeFilter.Add(".flac");
            fop.FileTypeFilter.Add(".mp3");


            var files = await fop.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                AudioPlayer.AppendFile(file);
            }
            AudioPlayer.AudioMediaPlayer.Play();
        }

        public void LoadPlayingFile(MediaPlaybackItem mpi)
        {
            if (mpi == null) return;
            AudioPlayer.AudioPlayerTimer?.Dispose();
            MediaItemDisplayProperties dp = mpi.GetDisplayProperties();
            AudioInfo ai = AudioPlayer.AudioInfos[mpi];
            this.Invoke((() =>
            {
                TbSingerName.Text = ai.Artist;
                TbSongName.Text = ai.SongName;
                AlbumImage.Source = ai.Picture;
                SliderAudioRate.Value = AudioPlayer.AudioMediaPlayer.Volume * 100;
                SliderProgress.Minimum = 0;
                SliderProgress.Maximum = ai.LengthInMilliseconds;
                ListBoxPlayList.SelectedIndex = (int)AudioPlayer.AudioMediaPlaybackList.CurrentItemIndex;
            }));
            AudioPlayer.AudioPlayerTimer = new Timer((state =>
            {
                this.Invoke(() =>
                {
                    try
                    {
                        var tai = (AudioInfo)state;
                        TbSingerName.Text = tai.Artist;
                        TbSongName.Text = tai.SongName;
                        SliderProgress.Value = AudioPlayer.AudioMediaPlayer.PlaybackSession.Position.TotalMilliseconds;
                        PlayStateIcon.Glyph = AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB4" : "\uEDB5";
                        //SliderAudioRate.Value = mp.Volume;
                    }
                    catch (Exception) { }
                });
            }), ai, 1000, 100);

        }

        public void RefreshSongList()
        {
            ListBoxPlayList.Items?.Clear();
            foreach (MediaPlaybackItem mediaPlaybackItem in AudioPlayer.AudioMediaPlaybackList.Items)
            {
                ListBoxPlayList.Items?.Add(mediaPlaybackItem.GetDisplayProperties().MusicProperties.Artist + " - " + mediaPlaybackItem.GetDisplayProperties().MusicProperties.Title);
            }
        }

        public void OnSongAdd()
        {
            this.Invoke((() =>
            {
                RefreshSongList();
            }));
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                AudioPlayer.AudioMediaPlayer.Pause();
            else if (AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                AudioPlayer.AudioMediaPlayer.Play();
            PlayStateIcon.Glyph = AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB5" : "\uEDB4";
        }

        private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            AudioPlayer.AudioMediaPlayer.Volume = SliderAudioRate.Value / 100;
        }

        private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.AudioMediaPlayer.IsMuted = !AudioPlayer.AudioMediaPlayer.IsMuted;
            BtnMuteIcon.Glyph = AudioPlayer.AudioMediaPlayer.IsMuted ? "\uE198" : "\uE15D";
            SliderAudioRate.Visibility = AudioPlayer.AudioMediaPlayer.IsMuted ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.AudioMediaPlaybackList.MovePrevious();
        }

        private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.AudioMediaPlaybackList.MoveNext();
        }

        private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListBoxPlayList.SelectedIndex != -1 && ListBoxPlayList.SelectedIndex != AudioPlayer.AudioMediaPlaybackList.CurrentItemIndex)
                AudioPlayer.AudioMediaPlaybackList.MoveTo((uint)ListBoxPlayList.SelectedIndex);
        }

        private void ButtonExpand_OnClick(object sender, RoutedEventArgs e)
        {
            ButtonExpand.Visibility = Visibility.Collapsed;
            ButtonCollapse.Visibility = Visibility.Visible;

            Common.PageMain.GridPlayBar.Background = null;
            //Common.PageMain.MainFrame.Visibility = Visibility.Collapsed;
            Common.PageMain.ExpandedPlayer.Visibility = Visibility.Visible;
            Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer), null,
                new EntranceNavigationTransitionInfo());
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongTitle", TbSongName);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongImg", AlbumImage);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongArtist", TbSingerName);
            Common.PageExpandedPlayer.StartExpandAnimation();
            GridSongInfo.Visibility = Visibility.Collapsed;
        }

        private void ButtonCollapse_OnClick(object sender, RoutedEventArgs e)
        {
            Common.PageExpandedPlayer.StartCollapseAnimation();
            GridSongInfo.Visibility = Visibility.Visible;
            var anim1 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongTitle");
            var anim2 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongImg");
            var anim3 = ConnectedAnimationService.GetForCurrentView().GetAnimation("SongArtist");
            anim3.Configuration = new DirectConnectedAnimationConfiguration();
            anim2.Configuration = new DirectConnectedAnimationConfiguration();
            anim1.Configuration = new DirectConnectedAnimationConfiguration();
            anim3?.TryStart(TbSingerName);
            anim1?.TryStart(TbSongName);
            anim2?.TryStart(AlbumImage);
            ButtonExpand.Visibility = Visibility.Visible;
            ButtonCollapse.Visibility = Visibility.Collapsed;
            Common.PageMain.ExpandedPlayer.Navigate(typeof(BlankPage));
            //Common.PageMain.MainFrame.Visibility = Visibility.Visible;
            Common.PageMain.ExpandedPlayer.Visibility = Visibility.Collapsed;
            Common.PageMain.GridPlayBar.Background = new Windows.UI.Xaml.Media.AcrylicBrush() { BackgroundSource = AcrylicBackgroundSource.Backdrop, TintOpacity = 0.67500003206078, TintLuminosityOpacity = 0.183000008692034, TintColor = Windows.UI.Color.FromArgb(255, 128, 128, 128), FallbackColor = Windows.UI.Color.FromArgb(255, 128, 128, 128) };
        }
    }

}
