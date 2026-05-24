using HyPlayer.Domain.Music;
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
        if (_items.Count == 0)
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
                NotifyAppendDone();
                nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
            }
        }

        if (nextIndex is null)
            return;

        HyPlayItem item;
        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = nextIndex.Value;
            item = _items[_nowPlayingIndex];
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        if (providerItem is not null)
            await _control.LoadAndPlayAsync(providerItem, removeCurrentSongs: true);
        else
            await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MovePreviousAsync()
    {
        if (_items.Count == 0)
            return;

        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        var prevIndex = _activeStrategy.GetPrevious(BuildStrategyContext());
        if (prevIndex is null)
            return;

        HyPlayItem item;
        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = prevIndex.Value;
            item = _items[_nowPlayingIndex];
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        if (providerItem is not null)
            await _control.LoadAndPlayAsync(providerItem, removeCurrentSongs: true);
        else
            await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    /// <inheritdoc />
    public async Task MoveToAsync(HyPlayItem item)
    {
        int index;
        lock (_lock)
        {
            index = _items.IndexOf(item);
        }
        if (index < 0)
            return;

        await MoveToIndexAsync(index);
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

    private async Task MoveToIndexAsync(int index)
    {
        // 中断正在进行的过渡
        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        HyPlayItem item;
        SingleSongBase? providerItem;
        lock (_lock)
        {
            _nowPlayingIndex = index;
            item = _items[_nowPlayingIndex];
            providerItem = _providerItems[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        if (providerItem is not null)
            await _control.LoadAndPlayAsync(providerItem, removeCurrentSongs: true);
        else
            await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    private void ExitPersonalFmForSourceChange()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm(clearPlaylist: false);
    }
}
