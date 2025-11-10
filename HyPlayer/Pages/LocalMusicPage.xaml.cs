#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class LocalMusicPage : Page, INotifyPropertyChanged, IDisposable
{
    private static readonly string[] supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };
    private readonly ObservableCollection<HyPlayItem> localHyItems = new();
    private string _notificationText;
    private Task CurrentFileScanTask;
    private CancellationTokenSource cancellationTokenSource = new();
    private CancellationToken _cancellationToken;
    private bool disposedValue = false;

    public LocalMusicPage()
    {
        InitializeComponent();
        _cancellationToken = cancellationTokenSource.Token;
    }
    public string NotificationText
    {
        get => _notificationText;
        set
        {
            _notificationText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (CurrentFileScanTask != null && CurrentFileScanTask.IsCompleted == false)
        {
            try
            {
                NotificationText = "正在等待本地扫描进程结束...";
                cancellationTokenSource.Cancel();
                await CurrentFileScanTask;
            }
            catch
            {
                CurrentFileScanTask = null;
            }
        }
        Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadPageFrame.Navigate(typeof(DownloadPage));
    }

    private void Playall_Click(object sender, RoutedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(LocalMusicPage));
        HyPlayList.RemoveAllSong();
        HyPlayList.List.AddRange(localHyItems);
        HyPlayList.SongMoveTo(0);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFileScanTask == null || CurrentFileScanTask.IsCompleted == true) CurrentFileScanTask = LoadLocalMusic(forceRefresh: true);
    }

    private async Task LoadLocalMusic(bool forceRefresh = false)
    {
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        NotificationText = "正在扫描...";
        localHyItems.Clear();
        var folder = !string.IsNullOrEmpty(Common.Setting.searchingDir)
            ? await StorageFolder.GetFolderFromPathAsync(Common.Setting.searchingDir)
            : KnownFolders.MusicLibrary;
        
        FileLoadingIndicateRing.Visibility = Visibility.Visible;
        FileLoadingIndicateRing.IsActive = true;
        
        var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, supportedFormats);
        queryOptions.FolderDepth = FolderDepth.Deep;
        var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

        // Create a unique cache key based on folder path
        var cacheKey = folder.Path.GetHashCode().ToString();
        
        // Try to load from cache first
        var cachedItems = await SimpleCacher.GetOrCreateCacheAsync<List<LocalMusicCacheItem>>(
            CacheType.LocalMusicScan, 
            cacheKey, 
            async () =>
            {
                // If cache doesn't exist or is expired, scan the files
                var cacheItems = new List<LocalMusicCacheItem>();
                
                if (!Common.Setting.localProgressiveLoad)
                {
                    foreach (var storageFile in files)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            var item = await HyPlayList.LoadStorageFile(storageFile);
                            var cacheItem = new LocalMusicCacheItem
                            {
                                FilePath = storageFile.Path,
                                Name = item.PlayItem.Name,
                                ArtistNames = item.PlayItem.Artist?.Select(a => a.name).ToList(),
                                AlbumName = item.PlayItem.Album?.name,
                                Bitrate = item.PlayItem.Bitrate,
                                Duration = item.PlayItem.LengthInMilliseconds,
                                FileType = storageFile.FileType,
                                TrackId = item.PlayItem.TrackId,
                                InfoTag = item.PlayItem.InfoTag
                            };
                            cacheItems.Add(cacheItem);
                        }
                        catch
                        {
                            //ignore
                        }
                    }
                }
                else
                {
                    // Progressive load mode - cache minimal info
                    foreach (var storageFile in files)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var cacheItem = new LocalMusicCacheItem
                        {
                            FilePath = storageFile.Path,
                            Name = storageFile.Name,
                            FileType = storageFile.FileType,
                            IsProgressive = true
                        };
                        cacheItems.Add(cacheItem);
                    }
                }
                
                return cacheItems;
            },
            TimeSpan.FromHours(24), // Cache for 24 hours
            forceRefresh: forceRefresh
        );

        // Build HyPlayItems from cached data
        if (cachedItems != null)
        {
            if (!Common.Setting.localProgressiveLoad)
            {
                foreach (var cacheItem in cachedItems)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var storageFile = await StorageFile.GetFileFromPathAsync(cacheItem.FilePath);
                        var item = new HyPlayItem
                        {
                            ItemType = HyPlayItemType.Local,
                            PlayItem = new PlayItem
                            {
                                Name = cacheItem.Name,
                                Artist = cacheItem.ArtistNames?.Select(name => new NCArtist { name = name, Type = HyPlayItemType.Local }).ToList(),
                                Album = new NCAlbum { name = cacheItem.AlbumName },
                                Bitrate = cacheItem.Bitrate,
                                LengthInMilliseconds = cacheItem.Duration,
                                SubExt = cacheItem.FileType,
                                TrackId = cacheItem.TrackId,
                                CDName = "01",
                                Url = cacheItem.FilePath,
                                InfoTag = cacheItem.InfoTag ?? "本地歌曲",
                                IsLocalFile = true,
                                Type = HyPlayItemType.Local,
                                DontSetLocalStorageFile = storageFile
                            }
                        };
                        localHyItems.Add(item);
                    }
                    catch
                    {
                        // File might have been deleted, skip it
                    }
                }
            }
            else
            {
                var undeterminedAlbum = new NCAlbum
                {
                    AlbumType = HyPlayItemType.LocalProgressive,
                    name = "未知专辑 - 播放后加载"
                };
                var undeterminedArtistList = new List<NCArtist>
                {
                    new()
                    {
                        name = "未知歌手 - 播放后加载",
                        Type = HyPlayItemType.LocalProgressive
                    }
                };
                
                foreach (var cacheItem in cachedItems)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var storageFile = await StorageFile.GetFileFromPathAsync(cacheItem.FilePath);
                        var item = new HyPlayItem
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
                                Name = cacheItem.Name,
                                CDName = "01",
                                Size = null,
                                SubExt = cacheItem.FileType,
                                TrackId = 0,
                                InfoTag = "本地歌曲",
                                Type = HyPlayItemType.LocalProgressive,
                                Url = cacheItem.FilePath
                            }
                        };
                        localHyItems.Add(item);
                    }
                    catch
                    {
                        // File might have been deleted, skip it
                    }
                }
            }
        }
        
        NotificationText = "扫描完成, 共 " + localHyItems.Count + " 首音乐";
        FileLoadingIndicateRing.IsActive = false;
        FileLoadingIndicateRing.Visibility = Visibility.Collapsed;
        ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
    }


    private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxLocalMusicContainer.SelectedIndex == -1) return;
        HyPlayList.RemoveAllSong();
        HyPlayList.List.AddRange(localHyItems);
        HyPlayList.SongMoveTo(ListBoxLocalMusicContainer.SelectedIndex);
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        var sf = await StorageFile.GetFileFromPathAsync((sender as Button).Tag as string);
        await CloudUpload.UploadMusic(sf);
    }

    private void Add_Local(object sender, RoutedEventArgs e)
    {
        _ = HyPlayList.PickLocalFile();
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                CurrentFileScanTask = null;
                cancellationTokenSource.Dispose();
                NotificationText = null;
                localHyItems.Clear();
            }
            ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
            disposedValue = true;
        }
    }

    ~LocalMusicPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Cache item for local music scan results
/// </summary>
public class LocalMusicCacheItem
{
    public string FilePath { get; set; }
    public string Name { get; set; }
    public List<string> ArtistNames { get; set; }
    public string AlbumName { get; set; }
    public int Bitrate { get; set; }
    public double Duration { get; set; }
    public string FileType { get; set; }
    public int TrackId { get; set; }
    public string InfoTag { get; set; }
    public bool IsProgressive { get; set; }
}