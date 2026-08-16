namespace HyPlayer.UI.Lists.IncrementalLoading;

public enum IncrementalLoadStatus
{
    Idle,
    InitialLoading,
    LoadingMore,
    Exhausted,
    Failed,
    Canceled
}
