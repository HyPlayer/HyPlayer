using System;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    public async Task OnTrackEndedAsync()
    {
        if (!await _trackEndLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (_activeStrategyId == "ltg")
                return;

            await MoveNextAsync(userInitiated: false).ConfigureAwait(false);
        }
        finally
        {
            _trackEndLock.Release();
        }
    }

    public void OnPositionTick(TimeSpan position, TimeSpan duration)
    {
        _control.CheckABTimeRemaining(position);
    }
}
