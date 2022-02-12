#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
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
public sealed partial class LocalMusicPage : Page
{
    private readonly List<HyPlayItem> localHyItems;
    private readonly List<ListViewPlayItem> localItems;
    private Task FileScanTask;
    private int index;

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
        if (FileScanTask != null) FileScanTask.Dispose();

        var folderName = await StorageFolder.GetFolderFromPathAsync(Common.Setting.searchingDir);
        var tmp = await folderName.GetItemsAsync();

        FileScanTask = Task.Run(() =>
        {
            _ = Common.Invoke(() =>
            {
                FileLoadingIndicateRing.IsActive = true;
                foreach (var item in tmp) GetSubFiles(item);
                FileLoadingIndicateRing.IsActive = false;
            }, CoreDispatcherPriority.Low);
        });
    }

    private bool CheckFileExtensionName(string fileName)
    {
        string[] supportedFormats = { ".flac", ".mp3", ".ncm", ".ape", ".m4a", ".wav" };
        foreach (var format in supportedFormats)
            if (fileName.EndsWith(format)) //检测扩展名是否支持
                return true;
        return false;
    }

    private async void GetSubFiles(IStorageItem item)
    {
        try
        {
            switch (item)
            {
                //检查文件扩展名，符合条件的才会在本地列表中显示
                case StorageFile file when CheckFileExtensionName(file.Name):
                {
                    var hyPlayItem = await HyPlayList.LoadStorageFile(file);
                    localHyItems.Add(hyPlayItem);
                    var listViewPlay = new ListViewPlayItem(hyPlayItem.PlayItem.Name, index++,
                        hyPlayItem.PlayItem.ArtistString);
                    localItems.Add(listViewPlay);
                    ListBoxLocalMusicContainer.Items?.Add(listViewPlay);
                    break;
                }
                case StorageFolder folder:
                {
                    foreach (var subitems in await folder.GetItemsAsync())
                        GetSubFiles(subitems);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private void ListBoxLocalMusicContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HyPlayList.RemoveAllSong();
        localHyItems.ForEach(t => HyPlayList.List.Add(t));
        HyPlayList.SongAppendDone();
        HyPlayList.SongMoveTo(ListBoxLocalMusicContainer.SelectedIndex);
    }

    private async void UploadCloud_Click(object sender, RoutedEventArgs e)
    {
        var sf = await StorageFile.GetFileFromPathAsync(localHyItems[int.Parse((sender as Button).Tag.ToString())]
            .PlayItem.Url);
        await CloudUpload.UploadMusic(sf);
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as Pivot).SelectedIndex == 1) LoadLocalMusic();
    }
}

public class ListViewPlayItem
{
    public ListViewPlayItem(string name, int index, string artist)
    {
        Name = name;
        Artist = artist;
        this.index = index;
    }

    public string Name { get; }
    public string Artist { get; }
    public string DisplayName => Artist + " - " + Name;

    public int index { get; }

    public override string ToString()
    {
        return Artist + " - " + Name;
    }
}