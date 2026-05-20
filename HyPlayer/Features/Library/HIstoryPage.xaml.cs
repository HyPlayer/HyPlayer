#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.User;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.History;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Library;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class HistoryPage : Page
{
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();

    private readonly ObservableCollection<NCSong> Songs = new();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _songRankWeekLoaderTask;
    private Task _songRankAllLoaderTask;

    public HistoryPage()
    {
        InitializeComponent();
        HisModeNavView.SelectedItem = SongHis;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_songRankWeekLoaderTask != null && !_songRankWeekLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _songRankWeekLoaderTask;
            }
            catch
            {
            }
        }
        if (_songRankAllLoaderTask != null && !_songRankAllLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _songRankAllLoaderTask;
            }
            catch
            {
            }
        }
        _cancellationTokenSource?.Dispose();
    }
    private async void NavigationView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        switch ((sender.SelectedItem?.As<NavigationViewItem>()).Name)
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
                Songsl.Clear();
                break;
            case "SongRankWeek":
                //听歌排行加载部分 - 优先级靠下
                _songRankWeekLoaderTask = LoadRankWeek();
                break;
            case "SongRankAll":
                //听歌排行加载部分 - 优先级靠下
                _songRankAllLoaderTask = LoadRankAll();
                break;
        }
    }

    private async Task LoadRankAll()
    {
        await LoadRank(async cancellationToken =>
        {
            var response = await _api.RequestAsync<UserRecordAllResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,
                new UserRecordRequest() { UserId = _auth.CurrentUser.Id, RecordType = UserRecordType.All }, cancellationToken);
            return (response.IsError, response.Error?.Message, response.Value?.AllData);
        }, record => record.Song.MapNcSong());
    }

    private async Task LoadRankWeek()
    {
        await LoadRank(async cancellationToken =>
        {
            var response = await _api.RequestAsync<UserRecordWeekResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,
                new UserRecordRequest() { UserId = _auth.CurrentUser.Id, RecordType = UserRecordType.WeekData }, cancellationToken);
            return (response.IsError, response.Error?.Message, response.Value?.WeekData);
        }, record => record.Song.MapNcSong());
    }

    private async Task LoadRank<TRecord>(Func<CancellationToken, Task<(bool IsError, string ErrorMessage, TRecord[] Records)>> requestRank, Func<TRecord, NCSong> mapSong)
    {
        Songs.Clear();
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await requestRank(_cancellationToken);
            if (result.IsError)
            {
                _notification.ShowMessage("获取播放记录失败", result.ErrorMessage);
                return;
            }
            var rankData = result.Records ?? [];
            for (var i = 0; i < rankData.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var song = mapSong(rankData[i]);
                song.Order = i;
                Songs.Add(song);
            }
        }
        catch (Exception ex) when (!(ex is TaskCanceledException or OperationCanceledException))
        {
            _notification.ShowMessage("获取播放记录失败", ex.Message);
        }
    }
}
