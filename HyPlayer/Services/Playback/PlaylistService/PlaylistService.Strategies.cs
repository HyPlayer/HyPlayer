namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 策略切换 ──────────────

    /// <inheritdoc />
    public void SetStrategy(string strategyId, bool persist = true)
    {
        if (!_strategies.TryGetValue(strategyId, out var strategy))
            return;

        _activeStrategy = strategy;
        _state.ActiveStrategyId = strategyId;
        if (persist)
            _setting.ActiveStrategyId = strategyId;
        _activeStrategy.OnPlaylistChanged(BuildStrategyContext());

        if (strategyId == "shn")
            CreateShufflePlayLists();
        else
            SendPlaylistChanged();
    }

    /// <inheritdoc />
    public void SetTransition(string transitionId)
    {
        if (!_transitions.TryGetValue(transitionId, out var transition))
            return;

        _activeTransition.Reset();
        _activeTransition = transition;
        _state.ActiveTransitionId = transitionId;
    }
}
