#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.History;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
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
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IAuthService _auth = Ioc.Default.GetRequiredService<IAuthService>();

    private readonly ObservableCollection<SongListItemViewModel> Songs = new();
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _songRankWeekLoaderTask;
    private Task _songRankAllLoaderTask;
    private string _currentSelectionName;
    private List<SingleSongBase> _songHistoryCache;

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
        var selectedName = (sender.SelectedItem?.As<NavigationViewItem>()).Name;
        if (string.Equals(_currentSelectionName, selectedName, StringComparison.Ordinal) && Songs.Count > 0)
            return;

        _currentSelectionName = selectedName;
        switch (selectedName)
        {
            case "SongHis":
                await LoadSongHistory(selectedName);
                break;
            case "SongRankWeek":
                //听歌排行加载部分 - 优先级靠下
                _songRankWeekLoaderTask ??= LoadRankWeek(selectedName);
                await _songRankWeekLoaderTask;
                break;
            case "SongRankAll":
                //听歌排行加载部分 - 优先级靠下
                _songRankAllLoaderTask ??= LoadRankAll(selectedName);
                await _songRankAllLoaderTask;
                break;
        }
    }

    private async Task LoadSongHistory(string selectionName)
    {
        _songHistoryCache ??= await HistoryManagement.GetSongHistory();
        if (!string.Equals(_currentSelectionName, selectionName, StringComparison.Ordinal))
            return;

        Songs.Clear();
        var songorder = 0;
        foreach (var song in _songHistoryCache)
        {
            Songs.Add(await SongListItemViewModel.FromProviderSongAsync(song, songorder++));
        }
    }

    private async Task LoadRankAll(string selectionName)
    {
        await LoadRank("all", selectionName);
    }

    private async Task LoadRankWeek(string selectionName)
    {
        await LoadRank("recent", selectionName);
    }

    private async Task LoadRank(string rangeId, string selectionName)
    {
        Songs.Clear();
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var container = new NeteaseUserLibrarySubContainer
            {
                ActualId = $"history-{rangeId}{_auth.CurrentUser.ActualId}",
                Name = "听歌排行",
                Kind = rangeId.Equals("recent", StringComparison.OrdinalIgnoreCase)
                    ? NeteaseUserLibrarySubContainer.ListeningHistoryRecentKind
                    : NeteaseUserLibrarySubContainer.ListeningHistoryAllKind,
                UserId = _auth.CurrentUser.ActualId,
                MaxProgressiveCount = 120
            };
            var rankData = await container.GetAllItemsAsync(_cancellationToken);
            if (!string.Equals(_currentSelectionName, selectionName, StringComparison.Ordinal))
                return;

            Songs.Clear();
            for (var i = 0; i < rankData.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Songs.Add(await MapHistoryItemToRowAsync(rankData[i], i));
            }
        }
        catch (Exception ex) when (!(ex is TaskCanceledException or OperationCanceledException))
        {
            _notification.ShowMessage("获取播放记录失败", ex.Message);
        }
    }

    private static async Task<SongListItemViewModel> MapHistoryItemToRowAsync(ProvidableItemBase item, int order)
    {
        if (item is SingleSongBase song)
            return await SongListItemViewModel.FromProviderSongAsync(song, order);

        return SongListItemViewModel.FromFallback(item.ActualId, item.Name, order);
    }
}
