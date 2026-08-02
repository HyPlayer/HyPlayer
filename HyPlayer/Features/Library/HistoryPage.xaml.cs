#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
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
    private readonly IUserLibraryProvidable _userLibraryProvider = Ioc.Default.GetRequiredService<IUserLibraryProvidable>();
    private readonly IUserLibraryTypeIds _userLibraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();

    public static readonly DependencyProperty HistoryContainerProperty = DependencyProperty.Register(
        nameof(HistoryContainer), typeof(ContainerBase), typeof(HistoryPage), new PropertyMetadata(default(ContainerBase)));

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _songRankWeekLoaderTask;
    private Task _songRankAllLoaderTask;
    private string _currentSelectionName;
    private List<ProvidableItemBase> _songHistoryCache;

    public HistoryPage()
    {
        InitializeComponent();
        HisModeNavView.SelectedItem = SongHis;
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public ContainerBase HistoryContainer
    {
        get => (ContainerBase)GetValue(HistoryContainerProperty);
        set => SetValue(HistoryContainerProperty, value);
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
        if (string.Equals(_currentSelectionName, selectedName, StringComparison.Ordinal) && HistoryContainer is not null)
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
        if (_songHistoryCache is null)
            _songHistoryCache = [.. await _history.GetSongHistoryAsync()];
        if (!string.Equals(_currentSelectionName, selectionName, StringComparison.Ordinal))
            return;

        HistoryContainer = new StaticItemsContainer(_songHistoryCache, "最近播放", "history");
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
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (_auth.CurrentUser?.ActualId is null)
                return;

            var libraryTypeId = rangeId.Equals("recent", StringComparison.OrdinalIgnoreCase)
                ? _userLibraryTypeIds.RecentListeningHistoryTypeId
                : _userLibraryTypeIds.AllListeningHistoryTypeId;
            if (await _userLibraryProvider.GetUserLibraryContainerAsync(_auth.CurrentUser.ActualId, libraryTypeId, _cancellationToken) is not ContainerBase container)
                return;

            var rankData = await LoadContainerItemsAsync(container);
            if (!string.Equals(_currentSelectionName, selectionName, StringComparison.Ordinal))
                return;

            HistoryContainer = new StaticItemsContainer(rankData, "听歌排行", rangeId);
        }
        catch (Exception ex) when (!(ex is TaskCanceledException or OperationCanceledException))
        {
            _notification.ShowMessage("获取播放记录失败", ex.Message);
        }
    }

    private async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container)
    {
        return container switch
        {
            LinerContainerBase liner => await liner.GetAllItemsAsync(_cancellationToken),
            IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount, _cancellationToken)).Item2,
            _ => []
        };
    }
}
