using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Windows.Storage;

namespace HyPlayer.Services.Playback;

public sealed partial class PlaylistService
{
    // ────────────── 曲目结束处理 ──────────────

    /// <inheritdoc />
    public async Task OnTrackEndedAsync()
    {
        if (!await _trackEndLock.WaitAsync(0)) return;

        _trackEndCts?.Cancel();
        _trackEndCts?.Dispose();
        _trackEndCts = new CancellationTokenSource();
        var ct = _trackEndCts.Token;

        try
        {
            var action = _activeStrategy.OnTrackEnded(BuildStrategyContext());

            switch (action)
            {
                case PlayStrategyAction.MoveNext:
                    if (ShouldReplaySingleItem())
                    {
                        await _control.SeekAsync(TimeSpan.Zero);
                        _control.Play();
                        break;
                    }

                    await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    break;

                case PlayStrategyAction.Replay:
                    await _control.SeekAsync(TimeSpan.Zero);
                    break;

                case PlayStrategyAction.LoadMore:
                    if (_activeStrategy is IAsyncPlayStrategy asyncStrategy)
                    {
                        var moreItems = await asyncStrategy.LoadMoreAsync(
                            BuildStrategyContext(), ct);
                        lock (_lock)
                        {
                            _items.AddRange(moreItems);
                        }
                        NotifyAppendDone();
                        await _activeTransition.OnTrackEndedAsync(BuildTransitionContext());
                    }
                    break;

                case PlayStrategyAction.Stop:
                    // 服务器驱动模式，不做任何操作
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 新的播放结束处理已接管。
        }
        finally
        {
            _trackEndLock.Release();
        }
    }

    /// <inheritdoc />
    public void OnPositionTick(TimeSpan position, TimeSpan duration)
    {
        if (!ShouldRunTransitionOnPositionTick())
            return;

        _activeTransition.OnPositionTick(BuildTransitionContext(position, duration));
    }

    private bool ShouldRunTransitionOnPositionTick()
    {
        var action = _activeStrategy.OnTrackEnded(BuildStrategyContext());
        return action is PlayStrategyAction.MoveNext or PlayStrategyAction.LoadMore;
    }
}
