#region

using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.User;
using HyPlayer.NeteaseApi.Bases;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class History : Page, IDisposable
{
    private readonly ObservableCollection<NCSong> Songs = new();
    private bool disposedValue = false;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _songRankWeekLoaderTask;
    private Task _songRankAllLoaderTask;

    public History()
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
        Dispose();
    }
    private async void NavigationView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(History));
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
        if (disposedValue) throw new ObjectDisposedException(nameof(History));
        Songs.Clear();
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ret3 = await Common.NeteaseAPI.RequestAsync<UserRecordAllResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,
                new UserRecordRequest() { UserId = Common.LoginedUser.id, RecordType = UserRecordType.All });
            if (ret3.IsError)
            {
                Common.AddToTeachingTipLists("获取播放记录失败", ret3.Error.Message);
                return;
            }
            var weekData = ret3.Value?.AllData;
            for (var i = 0; i < weekData.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var song = weekData[i].Song.MapNcSong();
                song.Order = i;
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadRankWeek()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(History));
        Songs.Clear();
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var ret2 = await Common.NeteaseAPI!.RequestAsync<UserRecordWeekResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,
                new UserRecordRequest() { UserId = Common.LoginedUser.id, RecordType = UserRecordType.WeekData }, _cancellationToken);
            if (ret2.IsError)
            {
                Common.AddToTeachingTipLists("获取播放记录失败", ret2.Error.Message);
                return;
            }
            var weekData = ret2.Value?.WeekData;
            for (var i = 0; i < weekData.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var song = weekData[i].Song.MapNcSong();
                song.Order = i;
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Songs.Clear();
                _cancellationTokenSource.Dispose();
            }
            disposedValue = true;
        }
    }

    ~History()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}