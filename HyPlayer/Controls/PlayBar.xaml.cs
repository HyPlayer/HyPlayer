using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.ApplicationModel.Contacts;
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
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
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
        private bool canslide;

        public PlayBar()
        {
            Common.BarPlayBar = this;
            HyPlayList.OnPlayItemAdd += RefreshSongList;
            this.InitializeComponent();
            HyPlayList.OnPlayItemChange += LoadPlayingFile;
            HyPlayList.OnPlayPositionChange += OnPlayPositionChange;
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
                HyPlayList.AppendFile(file);
            }
            HyPlayList.Player.Play();
        }

        public void OnPlayPositionChange(TimeSpan ts)
        {
            this.Invoke(() =>
            {
                try
                {
                    var tai = HyPlayList.NowPlayingItem.AudioInfo;
                    TbSingerName.Text = tai.Artist;
                    TbSongName.Text = tai.SongName;
                    SliderProgress.Value = HyPlayList.Player.PlaybackSession.Position.TotalMilliseconds;
                    PlayStateIcon.Glyph =
                        HyPlayList.Player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                            ? "\uEDB4"
                            : "\uEDB5";
                    //SliderAudioRate.Value = mp.Volume;
                }
                catch (Exception)
                {
                }
            });
        }

        public void LoadPlayingFile(HyPlayItem mpi)
        {
            if (mpi == null) return;
            MediaItemDisplayProperties dp = mpi.MediaItem.GetDisplayProperties();
            AudioInfo ai = mpi.AudioInfo;
            this.Invoke((() =>
            {
                TbSingerName.Text = ai.Artist;
                TbSongName.Text = ai.SongName;
                AlbumImage.Source = mpi.ItemType == HyPlayItemType.Local ? ai.BitmapImage : new BitmapImage(new Uri(ai.Picture));
                SliderAudioRate.Value = HyPlayList.Player.Volume * 100;
                SliderProgress.Minimum = 0;
                SliderProgress.Maximum = ai.LengthInMilliseconds;
                ListBoxPlayList.SelectedIndex = (int)HyPlayList.NowPlaying;
            }));
        }

        public void RefreshSongList(HyPlayItem hpi)
        {
            ObservableCollection<ListViewPlayItem> Contacts = new ObservableCollection<ListViewPlayItem>();
            for (int i = 0; i < HyPlayList.List.Count; i++)
            {
                Contacts.Add(new ListViewPlayItem(HyPlayList.List[i].Name, i, HyPlayList.List[i].AudioInfo.Artist));
            }
            ListBoxPlayList.ItemsSource = Contacts;
            ListBoxPlayList.SelectedIndex = HyPlayList.NowPlaying;
        }

        public async void Invoke(Action action, Windows.UI.Core.CoreDispatcherPriority Priority = Windows.UI.Core.CoreDispatcherPriority.Normal)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Priority, () => { action(); });
        }

        private void BtnPlayStateChange_OnClick(object sender, RoutedEventArgs e)
        {
            if (HyPlayList.isPlaying)
                HyPlayList.Player.Pause();
            else if (!HyPlayList.isPlaying)
                HyPlayList.Player.Play();
            PlayStateIcon.Glyph = HyPlayList.isPlaying ? "\uEDB5" : "\uEDB4";
        }

        private void SliderAudioRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            HyPlayList.Player.Volume = e.NewValue / 100;
            if (Common.PageExpandedPlayer != null)
                Common.PageExpandedPlayer.SliderVolumn.Value = e.NewValue;
        }

        private void BtnMute_OnCllick(object sender, RoutedEventArgs e)
        {
            HyPlayList.Player.IsMuted = !HyPlayList.Player.IsMuted;
            BtnMuteIcon.Glyph = HyPlayList.Player.IsMuted ? "\uE198" : "\uE15D";
            SliderAudioRate.Visibility = HyPlayList.Player.IsMuted ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnPreviousSong_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.PlaybackList.MovePrevious();
        }

        private void BtnNextSong_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.PlaybackList.MoveNext();
        }

        private void ListBoxPlayList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListBoxPlayList.SelectedIndex != -1 && ListBoxPlayList.SelectedIndex != HyPlayList.NowPlaying)
                HyPlayList.PlaybackList.MoveTo((uint)ListBoxPlayList.SelectedIndex);
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

        private void ButtonCleanAll_OnClick(object sender, RoutedEventArgs e)
        {
            HyPlayList.List.Clear();
            HyPlayList.SyncPlayList();
            ListBoxPlayList.ItemsSource = new ObservableCollection<ListViewPlayItem>();
        }

        private void ButtonAddLocal_OnClick(object sender, RoutedEventArgs e)
        {
            TestFile();
        }

        private void SliderProgress_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (canslide)
                HyPlayList.Player.PlaybackSession.Position = TimeSpan.FromMilliseconds(SliderProgress.Value);
        }

        private void SliderProgress_OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            canslide = true;
        }

        private void SliderProgress_OnManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            canslide = false;
        }

        private void PlayListRemove_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button btn)
                {
                    HyPlayList.List.RemoveAt(int.Parse(btn.Tag.ToString()));
                    HyPlayList.SyncPlayList();
                    RefreshSongList(null);
                }
            }
            catch { }

        }
    }



    public class ListViewPlayItem
    {
        public string Name { get; private set; }
        public string Artist { get; private set; }
        public string DisplayName => Artist + " - " + Name;

        public int index { get; private set; }

        public ListViewPlayItem(string name, int index, string artist)
        {
            Name = name;
            Artist = artist;
            this.index = index;
        }

        public override string ToString()
        {
            return Artist + " - " + Name;
        }
    }


}
