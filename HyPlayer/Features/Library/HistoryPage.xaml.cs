#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Application.Notifications;
using HyPlayer.Features.History.Services;
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
    private readonly IUserLibraryProvidable _userLibraryProvider = Ioc.Default.GetRequiredService<IUserLibraryProvidable>();
    private readonly IUserLibraryTypeIds _userLibraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();

    public static readonly DependencyProperty HistoryContainerProperty = DependencyProperty.Register(
        nameof(HistoryContainer), typeof(ContainerBase), typeof(HistoryPage), new PropertyMetadata(default(ContainerBase)));

    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken _cancellationToken;
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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
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
                await LoadRankWeek(selectedName);
                break;
            case "SongRankAll":
                await LoadRankAll(selectedName);
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
            var libraryTypeId = rangeId.Equals("recent", StringComparison.OrdinalIgnoreCase)
                ? _userLibraryTypeIds.RecentListeningHistoryTypeId
                : _userLibraryTypeIds.AllListeningHistoryTypeId;
            if (await _userLibraryProvider.GetCurrentUserLibraryContainerAsync(libraryTypeId, _cancellationToken)
                is not ContainerBase container)
                return;

            if (!string.Equals(_currentSelectionName, selectionName, StringComparison.Ordinal))
                return;

            HistoryContainer = container;
        }
        catch (Exception ex) when (!(ex is TaskCanceledException or OperationCanceledException))
        {
            _notification.ShowMessage("获取播放记录失败", ex.Message);
        }
    }

}
