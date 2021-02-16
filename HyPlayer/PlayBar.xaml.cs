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
using TagLib;
using File = TagLib.File;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer
{
    public class UwpStorageFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly StorageFile file;

        public string Name => file.Name;

        public Stream ReadStream
        {
            get
            {
                return file.OpenStreamForReadAsync().GetAwaiter().GetResult();
            }
        }

        public Stream WriteStream
        {
            get
            {
                return file.OpenStreamForWriteAsync().GetAwaiter().GetResult();
            }
        }


        public UwpStorageFileAbstraction(StorageFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            this.file = file;
        }


        public void CloseStream(Stream stream)
        {
            stream?.Dispose();
        }
    }

    public sealed partial class PlayBar : UserControl
    {
        private MediaPlayer mp;
        private Timer timer;
        public PlayBar()
        {
            this.InitializeComponent();
            ButtonBase_OnClick(null, null);
        }

        private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            FileOpenPicker fop = new FileOpenPicker();
            fop.FileTypeFilter.Add(".flac");
            fop.FileTypeFilter.Add(".mp3");
            StorageFile sf = await fop.PickSingleFileAsync();
            var afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            AudioInfo ai = new AudioInfo()
            {
                Album = afi.Tag.Album,
                Artist = string.Join('/', afi.Tag.Artists),
                LengthInMilliseconds = afi.Properties.Duration.Milliseconds,
                SongName = afi.Tag.Title,
                //Picture = afi.Tag.Pictures[0].Data
            };
            MediaSource ms = MediaSource.CreateFromStorageFile(sf);
            var _mediaPlaybackItem = new MediaPlaybackItem(ms);
            var properties = _mediaPlaybackItem.GetDisplayProperties();
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;
            _mediaPlaybackItem.ApplyDisplayProperties(properties);
            mp = new MediaPlayer()
            {
                Source = _mediaPlaybackItem
            };
            TbSingerName.Text = ai.Artist;
            TbSongName.Text = ai.SongName;
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile sampleFile =
                await storageFolder.CreateFileAsync("album.png",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
            sampleFile.OpenStreamForWriteAsync().Result.Write(afi.Tag.Pictures[0].Data.ToArray(),0, afi.Tag.Pictures[0].Data.ToArray().Length);
            AlbumImage.Source = new BitmapImage(new Uri(sampleFile.Path));
            mp.Play();
            timer = new Timer((state =>
            {
                this.Invoke(() =>
                {
                    var tai = (AudioInfo)state;
                    TbSingerName.Text = tai.Artist;
                    TbSongName.Text = tai.SongName;
                    double prog = (Math.Floor(mp.Position.TotalSeconds) * 100 / mp.NaturalDuration.TotalSeconds);
                    if (!double.IsNaN(prog))
                        SliderProgress.Value = prog;
                    PlayStateIcon.Glyph = mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB4" : "\uEDB5";
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
            if (mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                mp.Pause();
            else if (mp.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                mp.Play();
            PlayStateIcon.Glyph = mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uEDB5" : "\uEDB4";
        }

        private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            mp.Volume = SliderAudioRate.Value / 100;
        }

        private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
        {
            mp.IsMuted = !mp.IsMuted;
            BtnMuteIcon.Glyph = mp.IsMuted ? "\uE198" : "\uE15D";
            SliderAudioRate.Visibility = mp.IsMuted ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    struct AudioInfo
    {
        public string SongName;
        public string Artist;
        public string Album;
        public int LengthInMilliseconds;
        public Picture Picture;
    }
}
