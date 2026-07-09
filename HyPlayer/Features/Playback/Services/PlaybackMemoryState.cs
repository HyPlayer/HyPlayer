using System;
using System.Collections.Generic;

namespace HyPlayer.Features.Playback.Services;

public sealed record PlaybackItemIdentity(
    string ProviderId,
    string TypeId,
    string ActualId,
    string? LocalPath = null);

public sealed record PlaybackMemoryState(
    int Version,
    string? PlaySourceId,
    string? SourceKind,
    string? SourceId,
    List<PlaybackItemIdentity> Queue,
    PlaybackItemIdentity? CurrentItem,
    int CurrentIndex,
    long PositionMilliseconds,
    string? ActiveStrategyId,
    DateTimeOffset SavedAt);
