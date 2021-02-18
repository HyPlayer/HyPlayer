using System;
using System.Collections.Generic;
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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using Microsoft.Toolkit.Extensions;
using TagLib;
using File = TagLib.File;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer
{
    
    public sealed partial class PlayBar : UserControl
    {
        public PlayBar()
        {
            this.InitializeComponent();
            AudioPlayer.AudioMediaPlaybackList = new MediaPlaybackList();
            AudioPlayer.AudioMediaPlaybackList.ItemOpened += _mediaPlaybackList_ItemOpened;
            AudioPlayer.AudioMediaPlaybackList.CurrentItemChanged += _mediaPlaybackList_CurrentItemChanged;
            AudioPlayer.AudioMediaPlayer = new MediaPlayer()
            {
                Source = AudioPlayer.AudioMediaPlaybackList,
            };
            TestFile();
        }

        private void _mediaPlaybackList_CurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
        {
            LoadPlayingFile(args.NewItem);
        }

        private void _mediaPlaybackList_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            this.Invoke((() =>
            {
                ListBoxPlayList.Items?.Clear();
                foreach (MediaPlaybackItem mediaPlaybackItem in sender.Items)
                {
                    ListBoxPlayList.Items.Add(mediaPlaybackItem.GetDisplayProperties().MusicProperties.Artist +" - "+mediaPlaybackItem.GetDisplayProperties().MusicProperties.Title);
                }
                ListBoxPlayList.SelectedIndex = (int)AudioPlayer.AudioMediaPlaybackList.CurrentItemIndex;
            }));
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

        private async void LoadPlayingFile(MediaPlaybackItem mpi)
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
            }));

            //mp.Play();
            AudioPlayer.AudioPlayerTimer = new Timer((state =>
            {
                this.Invoke(() =>
                {
                    var tai = (AudioInfo)state;
                    TbSingerName.Text = tai.Artist;
                    TbSongName.Text = tai.SongName;
                    double prog = (Math.Floor(AudioPlayer.AudioMediaPlayer.PlaybackSession.Position.TotalSeconds) * 100 / AudioPlayer.AudioMediaPlayer.PlaybackSession.NaturalDuration.TotalSeconds);
                    if (!double.IsNaN(prog))
                        SliderProgress.Value = prog;
                    PlayStateIcon.Glyph = AudioPlayer.AudioMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB4" : "\uEDB5";
                    //SliderAudioRate.Value = mp.Volume;
                });
            }), ai, 1000, 1000);

        }



        private async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
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
            if (ListBoxPlayList.SelectedIndex != -1 && ListBoxPlayList.SelectedIndex!= AudioPlayer.AudioMediaPlaybackList.CurrentItemIndex)
                AudioPlayer.AudioMediaPlaybackList.MoveTo((uint)ListBoxPlayList.SelectedIndex);
        }
    }

}
