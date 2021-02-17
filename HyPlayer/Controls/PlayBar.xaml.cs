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
        MediaPlaybackList _mediaPlaybackList;
        private Timer timer;
        private Dictionary<MediaPlaybackItem, AudioInfo> audioInfos = new Dictionary<MediaPlaybackItem, AudioInfo>();
        private Random random = new Random();

        public PlayBar()
        {
            this.InitializeComponent();
            _mediaPlaybackList = new MediaPlaybackList();
            _mediaPlaybackList.ItemOpened += _mediaPlaybackList_ItemOpened;
            _mediaPlaybackList.CurrentItemChanged += _mediaPlaybackList_CurrentItemChanged;
            mp = new MediaPlayer()
            {
                Source = _mediaPlaybackList
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
                    ListBoxPlayList.Items.Add(mediaPlaybackItem.GetDisplayProperties().MusicProperties.Title);
                }
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
                AppendFile(file);
            }
            mp.Play();
        }

        private async void AppendFile(StorageFile sf)
        {
            var mediaPlaybackItem = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(sf));
            var afi = TagLib.File.Create(new UwpStorageFileAbstraction(sf), ReadStyle.Average);
            var properties = mediaPlaybackItem.GetDisplayProperties();
            AudioInfo ai = new AudioInfo()
            {
                Album = afi.Tag.Album,
                Artist = string.Join('/', afi.Tag.Artists),
                LengthInMilliseconds = afi.Properties.Duration.Milliseconds,
                SongName = afi.Tag.Title
            };
            properties.Type = MediaPlaybackType.Music;
            properties.MusicProperties.AlbumTitle = ai.Album;
            properties.MusicProperties.Artist = ai.Artist;
            properties.MusicProperties.Title = ai.SongName;
            /*
                MemoryStream st = new MemoryStream(afi.Tag.Pictures[0].Data.ToArray());
                IRandomAccessStream ras = st.AsRandomAccessStream();
                BitmapImage bi = new BitmapImage();
            */
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile sampleFile =
                await storageFolder.CreateFileAsync(ai.Artist + " - " + ai.Album + " - " + ai.SongName + ".png",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
            sampleFile.OpenStreamForWriteAsync().Result.Write(afi.Tag.Pictures[0].Data.ToArray(), 0, afi.Tag.Pictures[0].Data.ToArray().Length);
            ai.Picture = new BitmapImage(new Uri(sampleFile.Path));
            audioInfos[mediaPlaybackItem] = ai;
            properties.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(sampleFile);
            mediaPlaybackItem.ApplyDisplayProperties(properties);

            _mediaPlaybackList.Items.Add(mediaPlaybackItem);
        }

        private async void LoadPlayingFile(MediaPlaybackItem mpi)
        {
            timer?.Dispose();
            MediaItemDisplayProperties dp = mpi.GetDisplayProperties();
            AudioInfo ai = audioInfos[mpi];
            this.Invoke((() =>
            {
                TbSingerName.Text = ai.Artist;
                TbSongName.Text = ai.SongName;
                AlbumImage.Source = ai.Picture;
                SliderAudioRate.Value = mp.Volume * 100;
            }));

            //mp.Play();
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
        public BitmapImage Picture;
    }
}
