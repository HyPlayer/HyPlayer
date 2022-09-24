#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using NeteaseCloudMusicApi;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class History : Page
{
    private readonly ObservableCollection<NCSong> Songs;

    public History()
    {
        InitializeComponent();
        Songs = new ObservableCollection<NCSong>();
        HisModeNavView.SelectedItem = SongHis;
    }

    private async void NavigationView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        switch ((sender.SelectedItem as NavigationViewItem).Name)
        {
            case "SongHis":
                Songs.Clear();
                var Songsl = await HistoryManagement.GetNCSongHistory();
                var songorder = 0;
                foreach (var song in Songsl)
                {
                    song.Order = songorder++;
                    Songs.Add(song);
                }

                break;
            case "SongRankWeek":
                //听歌排行加载部分 - 优先级靠下
                _ = LoadRankWeek();
                break;
            case "SongRankAll":
                //听歌排行加载部分 - 优先级靠下
                _ = LoadRankAll();
                break;
        }
    }

    private async Task LoadRankAll()
    {
        Songs.Clear();
        try
        {
            var ret3 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "0" } });

            var weekData = ret3["allData"].ToArray();
            for (var i = 0; i < weekData.Length; i++)
            {
                var song = NCSong.CreateFromJson(weekData[i]["song"]);
                song.Order = i;
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadRankWeek()
    {
        Songs.Clear();
        try
        {
            var ret2 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.UserRecord,
                new Dictionary<string, object> { { "uid", Common.LoginedUser.id }, { "type", "1" } });

            var weekData = ret2["weekData"].ToArray();

            for (var i = 0; i < weekData.Length; i++)
            {
                var song = NCSong.CreateFromJson(weekData[i]["song"]);
                song.Order = i;
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }
}