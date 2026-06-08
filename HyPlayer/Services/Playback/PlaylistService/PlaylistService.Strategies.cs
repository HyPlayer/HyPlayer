namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 策略切换 ──────────────

    /// <inheritdoc />
    public void SetStrategy(string strategyId, bool persist = true)
    {
        if (strategyId is not ("seq" or "sgl" or "shn" or "pfm" or "ltg"))
            return;

        _activeStrategyId = strategyId;
        _state.ActiveStrategyId = strategyId;
        if (persist)
            _setting.ActiveStrategyId = strategyId;

        if (strategyId == "shn")
            CreateShufflePlayLists();
        else
            SendPlaylistChanged();
    }

    /// <inheritdoc />
    public void SetTransition(string transitionId)
    {
        if (transitionId is not ("dir" or "xfd" or "gap"))
            return;

        _activeTransitionId = transitionId;
        _state.ActiveTransitionId = transitionId;
    }
}
