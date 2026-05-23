using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
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
            var moreItems = (await asyncStrategy.LoadMoreAsync(BuildStrategyContext())).ToList();
            if (moreItems.Count > 0)
            {
                lock (_lock)
                {
                    _items.AddRange(moreItems);
                }
                NotifyAppendDone();
                nextIndex = _activeStrategy.GetNext(BuildStrategyContext());
            }
        }

        if (nextIndex is null)
            return;

        HyPlayItem item;
        lock (_lock)
        {
            _nowPlayingIndex = nextIndex.Value;
            item = _items[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

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
        lock (_lock)
        {
            _nowPlayingIndex = prevIndex.Value;
            item = _items[_nowPlayingIndex];
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

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

        // 中断正在进行的过渡
        await _activeTransition.OnManualSkipAsync(BuildTransitionContext());

        lock (_lock)
        {
            _nowPlayingIndex = index;
            SyncIndex();
        }
        SchedulePlayCorePlaylistSync();

        await _control.LoadAndPlayAsync(item, removeCurrentSongs: true);
    }

    private void ExitPersonalFmForSourceChange()
    {
        if (_state.IsInFm)
            PersonalFM.ExitFm(clearPlaylist: false);
    }
}
