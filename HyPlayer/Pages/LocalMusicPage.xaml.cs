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
        private readonly List<StorageFile> localMusicFiles;

        public LocalMusicPage()
        {
            InitializeComponent();
            localMusicFiles = new List<StorageFile>();
            localItems = new List<ListViewPlayItem>();
            localHyItems = new List<HyPlayItem>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DownloadPageFrame.Navigate(typeof(DownloadPage));
            //LoadLocalMusic();
        }

        private async void Playall_Click(object sender, RoutedEventArgs e)
        {
            HyPlayList.RemoveAllSong();
            foreach (var file in localMusicFiles) await HyPlayList.AppendFile(file);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
            ListBoxLocalMusicContainer.Items.Clear();
            localMusicFiles.Clear();
            localItems.Clear();
            localHyItems.Clear();
            index = 0;
            LoadLocalMusic();

            ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
        }

        private async void LoadLocalMusic()
        {
            FileLoadingIndicateRing.IsActive = true;
            foreach (var item in (await (await StorageFolder.GetFolderFromPathAsync(Common.Setting.downloadDir)).GetItemsAsync())) GetSubFiles(item);
            FileLoadingIndicateRing.IsActive = false;
        }

        private async void GetSubFiles(IStorageItem item)
        {
            try
            {
                if (item is StorageFile)
                {
                    var file = item as StorageFile;
                    if (file.FileType == ".mp3" || file.FileType == ".flac" || file.FileType == ".wav")
                    {
                        var mdp = await file.Properties.GetMusicPropertiesAsync();
                        string[] contributingArtistsKey = { "System.Music.Artist" };
                        var contributingArtistsProperty =
                            await mdp.RetrievePropertiesAsync(contributingArtistsKey);
                        var contributingArtists = contributingArtistsProperty["System.Music.Artist"] as string[];
                        if (contributingArtists is null) contributingArtists = new[] { "未知歌手" };

                        var ai = new AudioInfo
                        {
                            tag = "本地",
                            Album = string.IsNullOrEmpty(mdp.Album) ? "未知专辑" : mdp.Album,
                            ArtistArr = contributingArtists,
                            Artist = string.IsNullOrEmpty(string.Join('/', contributingArtists))
                                ? "未知歌手"
                                : string.Join('/', contributingArtists),
                            LengthInMilliseconds = mdp.Duration.TotalMilliseconds,
                            SongName = string.IsNullOrEmpty(mdp.Title) ? file.DisplayName : mdp.Title,
                            LocalSongFile = file
                        };
                        try
                        {
                            var lrcfile =
                                await (await file.GetParentAsync()).GetFileAsync(
                                    Path.ChangeExtension(file.Name, "lrc"));
                            ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }

                        var hyPlayItem = new HyPlayItem
                        {
                            AudioInfo = ai,
                            ItemType = HyPlayItemType.Local,
                            Name = ai.SongName,
                            Path = file.Path
                        };
                        localMusicFiles.Add(file);
                        localHyItems.Add(hyPlayItem);
                        var listViewPlay = new ListViewPlayItem(hyPlayItem.Name, index++, hyPlayItem.AudioInfo.Artist);
                        localItems.Add(listViewPlay);
                        ListBoxLocalMusicContainer.Items.Add(listViewPlay);
                    }
                }
                else if (item is StorageFolder)
                {
                    foreach (var subitems in await ((StorageFolder)item).GetItemsAsync())
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
    }
}