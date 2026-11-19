#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.Application.Loading;
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
    private readonly LoadState _load = new("播放记录", Ioc.Default.GetRequiredService<ILoadEnvironment>());
    private readonly IUserLibraryProvidable _userLibraryProvider = Ioc.Default.GetRequiredService<IUserLibraryProvidable>();
    private readonly IUserLibraryTypeIds _userLibraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
    private readonly IHistoryService _history = Ioc.Default.GetRequiredService<IHistoryService>();

    public static readonly DependencyProperty HistoryContainerProperty = DependencyProperty.Register(
        nameof(HistoryContainer), typeof(ContainerBase), typeof(HistoryPage), new PropertyMetadata(default(ContainerBase)));

    private string _currentSelectionName;
    private List<ProvidableItemBase> _songHistoryCache;

    public HistoryPage()
    {
        InitializeComponent();
        ItemsList.ContainerLoad = _load;
        HisModeNavView.SelectedItem = SongHis;
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
        ItemsList.ReleaseResources();
        _songHistoryCache = null;
    }

    private void NavigationView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        var selectedName = (sender.SelectedItem?.As<NavigationViewItem>()).Name;
        if (string.Equals(_currentSelectionName, selectedName, StringComparison.Ordinal)
            && (HistoryContainer is not null || _load.IsLoading))
            return;

        _currentSelectionName = selectedName;
        var isRank = selectedName != "SongHis";
        _load.Subject = isRank ? "听歌排行" : "播放记录";
        _load.EmptyText = isRank ? "还没有听歌排行" : "还没有播放记录";
        _load.RequiresAccount = isRank;
        ItemsList.EmptyText = _load.EmptyText;
        HistoryContainer = null;
        _ = _load.RunAsync(
            cancellationToken => selectedName switch
            {
                "SongRankWeek" => LoadRank(_userLibraryTypeIds.RecentListeningHistoryTypeId, cancellationToken),
                "SongRankAll" => LoadRank(_userLibraryTypeIds.AllListeningHistoryTypeId, cancellationToken),
                _ => LoadSongHistory()
            },
            container => HistoryContainer = container,
            static container => container is null);
    }

    private async Task<ContainerBase?> LoadSongHistory()
    {
        _songHistoryCache ??= [.. await _history.GetSongHistoryAsync()];
        return new StaticItemsContainer(_songHistoryCache, "最近播放", "history");
    }

    private Task<ContainerBase?> LoadRank(string libraryTypeId, CancellationToken cancellationToken)
    {
        return _userLibraryProvider.GetCurrentUserLibraryContainerAsync(libraryTypeId, cancellationToken);
    }

}
