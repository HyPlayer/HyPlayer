#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class LocalMusicPage : Page, INotifyPropertyChanged
{
    private readonly ObservableCollection<HyPlayItem> localHyItems;
    private Task FileScanTask;
    private int index;
    private string _notificationText;
    private static string[] supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };

    public string NotificationText
    {
        get => _notificationText;
        set
        {
            _notificationText = value;
            OnPropertyChanged();
        }
    }

    public LocalMusicPage()
    {
        InitializeComponent();
        localHyItems = new();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        localHyItems.Clear();
        FileScanTask?.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadPageFrame.Navigate(typeof(DownloadPage));
    }

    private void Playall_Click(object sender, RoutedEventArgs e)
    {
        HyPlayList.RemoveAllSong();
        HyPlayList.List.AddRange(localHyItems);
        HyPlayList.SongAppendDone();
        HyPlayList.SongMoveTo(0);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        if (ListBoxLocalMusicContainer.Items != null) ListBoxLocalMusicContainer.Items.Clear();
        localHyItems.Clear();
        index = 0;
        LoadLocalMusic();

        ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
    }

    private async Task LoadLocalMusic()
    {
        FileScanTask?.Dispose();
        NotificationText = "正在扫描...";
        ListBoxLocalMusicContainer.ItemsSource = localHyItems;
        var folder = (!string.IsNullOrEmpty(Common.Setting.searchingDir))
            ? await StorageFolder.GetFolderFromPathAsync(Common.Setting.searchingDir)
            : KnownFolders.MusicLibrary;
        // Use Query to boost? maybe?
        FileLoadingIndicateRing.Visibility = Visibility.Visible;
        FileLoadingIndicateRing.IsActive = true;
        QueryOptions queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, supportedFormats);
        queryOptions.FolderDepth = FolderDepth.Deep;
        var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

        if (!Common.Setting.localProgressiveLoad)
        {
            foreach (var storageFile in files)
            {
                try
                {
                    var item = await HyPlayList.LoadStorageFile(storageFile);
                    localHyItems.Add(item);
                }
                catch
                {
                    //ignore
                }
            }
        }
        else
        {
            var undeterminedAlbum = new NCAlbum
            {
                AlbumType = HyPlayItemType.LocalProgressive,
                name = "未知专辑"
            };
            var undeterminedArtistList = new List<NCArtist>()
            {
                new NCArtist
                {
                    name = "未知歌手",
                    Type = HyPlayItemType.LocalProgressive
                }
            };
            foreach (var storageFile in files)
            {
                localHyItems.Add(new HyPlayItem
                {
                    ItemType = HyPlayItemType.LocalProgressive,
                    PlayItem = new PlayItem
                    {
                        Album = undeterminedAlbum,
                        Artist = undeterminedArtistList,
                        Bitrate = 0,
                        DontSetLocalStorageFile = storageFile,
                        IsLocalFile = true,
                        LengthInMilliseconds = 0,
                        Name = storageFile.Name,
                        CDName = "01",
                        Size = null,
                        SubExt = storageFile.FileType,
                        TrackId = 0,
                        Tag = "本地歌曲",
                        Type = HyPlayItemType.LocalProgressive,
                        Url = storageFile.Path
                    }
                });
            }
        }

        NotificationText = "扫描完成, 共 " + files.Count + " 首音乐";
        FileLoadingIndicateRing.IsActive = false;
        FileLoadingIndicateRing.Visibility = Visibility.Collapsed;
    }


    private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HyPlayList.RemoveAllSong();
        HyPlayList.List.AddRange(localHyItems);
        HyPlayList.SongAppendDone();
        HyPlayList.SongMoveTo(ListBoxLocalMusicContainer.SelectedIndex);
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        var sf = await StorageFile.GetFileFromPathAsync((((sender as Button).Tag) as HyPlayItem)
            .PlayItem.Url);
        await CloudUpload.UploadMusic(sf);
    }

    private void Add_Local(object sender, RoutedEventArgs e)
    {
        HyPlayList.PickLocalFile();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}