#region

using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
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
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class LocalMusicPage : Page, INotifyPropertyChanged
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();

    private static readonly string[] supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };
    private readonly ObservableCollection<HyPlayItem> localHyItems = new();
    private string _notificationText;
    private Task CurrentFileScanTask;
    private CancellationTokenSource cancellationTokenSource = new();
    private CancellationToken _cancellationToken;

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
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        cancellationTokenSource.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DownloadPageFrame.Navigate(typeof(DownloadPage));
    }

    private void Playall_Click(object sender, RoutedEventArgs e)
    {
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        _playlist.Clear();
        _playlist.AppendItems(localHyItems);
        _ = _playlist.MoveToAsync(localHyItems.FirstOrDefault());
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFileScanTask == null || CurrentFileScanTask.IsCompleted == true) CurrentFileScanTask = LoadLocalMusic();
    }

    private async Task LoadLocalMusic()
    {
        ListBoxLocalMusicContainer.SelectionChanged -= ListBoxLocalMusicContainer_SelectionChanged;
        NotificationText = "正在扫描...";
        localHyItems.Clear();
        var folder = !string.IsNullOrEmpty(_setting.searchingDir)
            ? await StorageFolder.GetFolderFromPathAsync(_setting.searchingDir)
            : KnownFolders.MusicLibrary;
        // Use Query to boost? maybe?
        FileLoadingIndicateRing.Visibility = Visibility.Visible;
        FileLoadingIndicateRing.IsActive = true;
        var queryOptions = new QueryOptions(CommonFileQuery.DefaultQuery, supportedFormats);
        queryOptions.FolderDepth = FolderDepth.Deep;
        var files = await folder.CreateFileQueryWithOptions(queryOptions).GetFilesAsync();

        if (!_setting.localProgressiveLoad)
        {
            foreach (var storageFile in files)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var item = await Ioc.Default.GetRequiredService<IPlaylistService>().LoadStorageFileAsync(storageFile);
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
                Name = "未知专辑 - 播放后加载"
            };
            var undeterminedArtistList = new List<NCArtist>
            {
                new()
                {
                    Name = "未知歌手 - 播放后加载",
                    Type = HyPlayItemType.LocalProgressive
                }
            };
            foreach (var storageFile in files)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var item = new HyPlayItem
                {
                    ItemType = HyPlayItemType.LocalProgressive,
                    Album = undeterminedAlbum,
                    Artist = undeterminedArtistList,
                    Bitrate = 0,
                    LocalStorageFile = storageFile,
                    IsLocalFile = true,
                    LengthInMilliseconds = 0,
                    Name = storageFile.Name,
                    CDName = "01",
                    Size = 0,
                    SubExt = storageFile.FileType,
                    TrackId = 0,
                    InfoTag = "本地歌曲",
                    Url = storageFile.Path
                };
                localHyItems.Add(item);
            }
        }
        NotificationText = "扫描完成, 共 " + files.Count + " 首音乐";
        FileLoadingIndicateRing.IsActive = false;
        FileLoadingIndicateRing.Visibility = Visibility.Collapsed;
        ListBoxLocalMusicContainer.SelectionChanged += ListBoxLocalMusicContainer_SelectionChanged;
    }


    private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListBoxLocalMusicContainer.SelectedItem == null) return;
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        _playlist.Clear();
        _playlist.AppendItems(localHyItems);
        _ = _playlist.MoveToAsync(ListBoxLocalMusicContainer.SelectedItem as HyPlayItem);
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        var sf = await StorageFile.GetFileFromPathAsync((sender?.As<Button>()).Tag as string);
        await CloudUpload.UploadMusic(sf);
    }

    private void Add_Local(object sender, RoutedEventArgs e)
    {
        _ = Ioc.Default.GetRequiredService<IPlaylistService>().PickLocalFileAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}