using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Application.Notifications;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

using HyPlayer.UI.Lists;
namespace HyPlayer.Features.Library;

public enum HistoryMode
{
    Recent,
    WeeklyRanking,
    AllRanking
}

public partial class HistoryViewModel(
    INotificationService notification,
    IAuthService auth,
    IUserLibraryProvidable userLibraryProvider,
    IUserLibraryTypeIds userLibraryTypeIds,
    IHistoryService history) : ObservableObject
{
    private readonly Dictionary<HistoryMode, ContainerBase> _containers = [];

    [ObservableProperty] public partial ContainerBase? ContentContainer { get; set; }
    [ObservableProperty] public partial HistoryMode SelectedMode { get; set; }

    public async Task SelectModeAsync(HistoryMode mode, CancellationToken cancellationToken)
    {
        SelectedMode = mode;
        if (_containers.TryGetValue(mode, out var cached))
        {
            ContentContainer = cached;
            return;
        }

        try
        {
            var container = mode == HistoryMode.Recent
                ? await LoadRecentAsync(cancellationToken)
                : await LoadRankingAsync(mode, cancellationToken);
            if (container is null || SelectedMode != mode)
                return;

            _containers[mode] = container;
            ContentContainer = container;
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or OperationCanceledException))
        {
            notification.ShowMessage("获取播放记录失败", ex.Message);
        }
    }

    private async Task<ContainerBase> LoadRecentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var songs = await history.GetSongHistoryAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return new StaticItemsContainer([.. songs], "最近播放", "history");
    }

    private async Task<ContainerBase?> LoadRankingAsync(
        HistoryMode mode,
        CancellationToken cancellationToken)
    {
        if (auth.CurrentUser?.ActualId is not { } userId)
            return null;

        var typeId = mode == HistoryMode.WeeklyRanking
            ? userLibraryTypeIds.RecentListeningHistoryTypeId
            : userLibraryTypeIds.AllListeningHistoryTypeId;
        if (await userLibraryProvider.GetUserLibraryContainerAsync(userId, typeId, cancellationToken)
            is not ContainerBase source)
            return null;

        var items = await LoadContainerItemsAsync(source, cancellationToken);
        return new StaticItemsContainer(items, "听歌排行", mode == HistoryMode.WeeklyRanking ? "recent" : "all");
    }

    private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(
        ContainerBase container,
        CancellationToken cancellationToken)
    {
        return container switch
        {
            LinerContainerBase liner => await liner.GetAllItemsAsync(cancellationToken),
            IProgressiveLoadingContainer progressive =>
                (await progressive.GetProgressiveItemsListAsync(
                    0, progressive.MaxProgressiveCount, cancellationToken)).Item2,
            _ => []
        };
    }
}
