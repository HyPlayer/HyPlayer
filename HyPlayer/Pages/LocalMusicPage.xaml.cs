using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using HyPlayer.Classes;
using System.Threading.Tasks;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    ///     可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class LocalMusicPage : Page
    {
        private int index;
        private readonly List<HyPlayItem> localHyItems;
        private readonly List<ListViewPlayItem> localItems;
        private Task FileScanTask;

        public LocalMusicPage()
        {
            InitializeComponent();
            localItems = new List<ListViewPlayItem>();
            localHyItems = new List<HyPlayItem>();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            localItems.Clear();
            localHyItems.Clear();
            FileScanTask.Dispose();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DownloadPageFrame.Navigate(typeof(DownloadPage));
        }

        private async void Playall_Click(object sender, RoutedEventArgs e)
        {
            HyPlayList.RemoveAllSong();
            localHyItems.AddRange(localHyItems);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
            if (ListBoxLocalMusicContainer.Items != null) ListBoxLocalMusicContainer.Items.Clear();
            localItems.Clear();
            localHyItems.Clear();
            index = 0;
            LoadLocalMusic();

            ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
        }

        private async void LoadLocalMusic()
        {
            if (FileScanTask != null)
            {
                FileScanTask.Dispose();
            }

            var tmp = await KnownFolders.MusicLibrary.GetItemsAsync();
            FileScanTask = Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    FileLoadingIndicateRing.IsActive = true;
                    foreach (var item in tmp) GetSubFiles(item);
                    FileLoadingIndicateRing.IsActive = false;
                }, Windows.UI.Core.CoreDispatcherPriority.Low);
            });
        }

        private async void GetSubFiles(IStorageItem item)
        {
            try
            {
                if (item is StorageFile)
                {
                    
                    var file = item as StorageFile;
                    var hyPlayItem = await HyPlayList.LoadStorageFile(file);
                    localHyItems.Add(hyPlayItem);
                    var listViewPlay = new ListViewPlayItem(hyPlayItem.PlayItem.Name, index++,
                        hyPlayItem.PlayItem.ArtistString);
                    localItems.Add(listViewPlay);
                    if (ListBoxLocalMusicContainer.Items != null) ListBoxLocalMusicContainer.Items.Add(listViewPlay);
                }
                else if (item is StorageFolder)
                {
                    foreach (var subitems in await ((StorageFolder) item).GetItemsAsync())
                        GetSubFiles(subitems);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HyPlayList.List.Clear();
            localHyItems.ForEach(t => HyPlayList.List.Add(t));
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(ListBoxLocalMusicContainer.SelectedIndex);
        }

        private async void UploadCloud_Click(object sender, RoutedEventArgs e)
        {
            var sf = await StorageFile.GetFileFromPathAsync(localHyItems[int.Parse((sender as Button).Tag.ToString())].PlayItem.url);
            await CloudUpload.UploadMusic(sf);
        }

        private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as Pivot).SelectedIndex == 1)
            {
                LoadLocalMusic();
            }
        }
    }
}