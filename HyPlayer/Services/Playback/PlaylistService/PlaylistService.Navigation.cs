using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 导航 ──────────────

    /// <inheritdoc />
    public async Task MoveNextAsync(bool userInitiated = false)
    {
        if (_providerItems.Count == 0)
            return;

        if (userInitiated)
            await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
        if (nextIndex is null && _activeStrategy is IAsyncPlayStrategy asyncStrategy)
        {
            var moreItems = (await asyncStrategy.LoadMoreProviderItemsAsync(BuildStrategyContext())).ToList();
            if (moreItems.Count > 0)
            {
                lock (_lock)
                {
                    InsertQueueItems(moreItems);
                }
                PublishPlaylistChanged();
                nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
            }
        }

        if (nextIndex is null)
            return;

        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = nextIndex.Value;
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        await LoadQueueEntryAsync(providerItem);
    }

    /// <inheritdoc />
    public async Task MovePreviousAsync()
    {
        if (_providerItems.Count == 0)
            return;

        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var prevIndex = _activeStrategy.GetPrevious(BuildStrategyContext());
        if (prevIndex is null)
            return;

        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = prevIndex.Value;
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        await LoadQueueEntryAsync(providerItem);
    }

    /// <inheritdoc />
    public Task MoveToAsync(ProvidableItemBase item)
    {
        int index;
        lock (_lock)
        {
            index = _providerItems.FindIndex(providerItem => providerItem is not null &&
                providerItem.ProviderId == item.ProviderId &&
                providerItem.TypeId == item.TypeId &&
                providerItem.ActualId == item.ActualId);
        }

        return index >= 0 ? MoveToIndexAsync(index) : Task.CompletedTask;
    }

    public async Task MoveToIndexAsync(int index)
    {
        // 中断正在进行的过渡
        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = index;
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        await LoadQueueEntryAsync(providerItem);
    }

    private Task LoadQueueEntryAsync(SingleSongBase? providerItem)
    {
        return providerItem is not null
            ? _control.LoadAndPlayAsync(providerItem, removeCurrentSongs: true)
            : Task.CompletedTask;
    }

    private void ExitPersonalFmForSourceChange()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm(clearPlaylist: false);
    }
}
