using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class LocalMusicPage : Page
    {
        private List<StorageFile> localMusicFiles;
        private List<ListViewPlayItem> localItems;
        private List<HyPlayItem> localHyItems;
        private int index = 0;
        public LocalMusicPage()
        {
            this.InitializeComponent();
            localMusicFiles = new List<StorageFile>();
            localItems = new List<ListViewPlayItem>();
            localHyItems = new List<HyPlayItem>();
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DownloadPageFrame.Navigate(typeof(DownloadPage));
            LoadLocalMusic();
        }

        private async void Playall_Click(object sender, RoutedEventArgs e)
        {
            HyPlayList.RemoveAllSong();
            foreach (StorageFile file in localMusicFiles)
            {
                await HyPlayList.AppendFile(file);
            }
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
            foreach (IStorageItem item in await KnownFolders.MusicLibrary.GetItemsAsync())
            {
                GetSubFiles(item);
            }
            FileLoadingIndicateRing.IsActive = false;
        }
        private async void GetSubFiles(IStorageItem item)
        {
            try
            {
                if (item is StorageFile)
                {
                    StorageFile file = item as StorageFile;
                    if (file.FileType == ".mp3" || file.FileType == ".flac" || file.FileType == ".wav")
                    {
                        var mdp = await file.Properties.GetMusicPropertiesAsync();
                        string[] contributingArtistsKey = { "System.Music.Artist" };
                        IDictionary<string, object> contributingArtistsProperty =
                            await mdp.RetrievePropertiesAsync(contributingArtistsKey);
                        string[] contributingArtists = contributingArtistsProperty["System.Music.Artist"] as string[];
                        if (contributingArtists is null)
                        {
                            contributingArtists = new[] { "未知歌手" };
                        }

                        AudioInfo ai = new AudioInfo()
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
                            StorageFile lrcfile =
                                await (await file.GetParentAsync()).GetFileAsync(Path.ChangeExtension(file.Name, "lrc"));
                            ai.Lyric = await FileIO.ReadTextAsync(lrcfile);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(ex.Message);
                        }
                        HyPlayItem hyPlayItem = new HyPlayItem()
                        {
                            AudioInfo = ai,
                            isOnline = false,
                            ItemType = HyPlayItemType.Local,
                            Name = ai.SongName,
                            Path = file.Path
                        };
                        localMusicFiles.Add(file);
                        localHyItems.Add(hyPlayItem);
                        ListViewPlayItem listViewPlay = new ListViewPlayItem(hyPlayItem.Name, index++, hyPlayItem.AudioInfo.Artist);
                        localItems.Add(listViewPlay);
                        ListBoxLocalMusicContainer.Items.Add(listViewPlay);
                    }
                }
                else if (item is StorageFolder)
                {
                    foreach (IStorageItem subitems in await ((StorageFolder)item).GetItemsAsync())
                        GetSubFiles(subitems);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private async void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await HyPlayList.AppendFile(localMusicFiles[ListBoxLocalMusicContainer.SelectedIndex]);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(HyPlayList.List.Count - 1);
        }
    }
}
