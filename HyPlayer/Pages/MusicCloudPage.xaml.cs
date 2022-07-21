#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MusicCloudPage : Page, IDisposable
{
    private ObservableCollection<NCSong> Items = new();
    private int page;

    public MusicCloudPage()
    {
        InitializeComponent();
        SongContainer.ListSource = "content";
        SongContainer.Songs = Items;
    }

    public void Dispose()
    {
        Items = null;
    }

    public async Task LoadMusicCloudItem()
    {
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserCloud,
                new Dictionary<string, object>
                {
                    { "limit", 200 },
                    { "offset", page * 200 }
                });
            var idx = page * 200;
            foreach (var jToken in json["data"])
                try
                {
                    var ret = NCSong.CreateFromJson(jToken["simpleSong"]);
                    if (ret.Artist[0].id == "0")
                    {
                        //不是标准歌曲
                        ret.Album.name = jToken["album"]?.ToString();
                        ret.Artist.Clear();
                        ret.Artist.Add(new NCArtist
                        {
                            name = jToken["artist"]?.ToString()
                        });
                    }

                    ret.IsCloud = true;
                    ret.Order = idx++;
                    SongContainer.Songs.Add(ret);
                }
                catch
                {
                    //ignore
                }

            NextPage.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadMusicCloudItem();
    }


    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        LoadMusicCloudItem();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        DownloadManager.AddDownload(Items.ToList());
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".flac");
        fop.FileTypeFilter.Add(".mp3");
        fop.FileTypeFilter.Add(".ncm");
        fop.FileTypeFilter.Add(".ape");
        fop.FileTypeFilter.Add(".m4a");
        fop.FileTypeFilter.Add(".wav");


        var files =
            await fop.PickMultipleFilesAsync();
        Common.AddToTeachingTipLists("请稍等", "正在上传 " + files.Count + " 个音乐文件");
        for (var i = 0; i < files.Count; i++)
        {
            Common.AddToTeachingTipLists("正在上传共 " + files.Count + " 个音乐文件", "正在上传 第" + i + " 个音乐文件");
            await CloudUpload.UploadMusic(files[i]);
        }

        Common.AddToTeachingTipLists("上传完成", "请重新加载云盘页面");
    }
}