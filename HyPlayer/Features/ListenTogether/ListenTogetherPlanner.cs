using System;
using System.Collections.Generic;
using System.Linq;
using HyPlayer.PlayCore.Abstraction.Models;

namespace HyPlayer.Features.ListenTogether;

internal readonly record struct ListenTogetherPlaybackSnapshot(string SongId, bool IsPlaying, TimeSpan Position, long SeekVersion);

internal static class ListenTogetherCommands
{
    public const string Goto = "goto";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";
}

internal static class ListenTogetherPlanner
{
    public static readonly TimeSpan DriftTolerance = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan AbandonedSongWindow = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RoomHandoverWindow = TimeSpan.FromSeconds(20);
    private const long MinimumEpochMilliseconds = 1_000_000_000_000;
    private static readonly TimeSpan MaximumExtrapolation = TimeSpan.FromHours(6);

    public static string? DetectLocalCommand(ListenTogetherPlaybackSnapshot? published, ListenTogetherPlaybackSnapshot current)
    {
        if (string.IsNullOrEmpty(current.SongId)) return null;
        if (published is not { } last || last.SongId != current.SongId) return ListenTogetherCommands.Goto;
        if (last.IsPlaying != current.IsPlaying)
            return current.IsPlaying ? ListenTogetherCommands.Play : ListenTogetherCommands.Pause;
        return last.SeekVersion != current.SeekVersion ? ListenTogetherCommands.Seek : null;
    }

    public static bool IsOwnCommand(ProviderListenTogetherRemoteCommand command, string? myUserId,
        IReadOnlyCollection<long> sentClientSeqs)
    {
        if (!string.IsNullOrEmpty(command.UserId) && !string.IsNullOrEmpty(myUserId) && command.UserId != myUserId)
            return false;
        return command.ClientSeq > 0 && sentClientSeqs.Contains(command.ClientSeq);
    }

    public static bool ShouldApply(ProviderListenTogetherRemoteCommand command, long lastServerSeq, string? myUserId,
        IReadOnlyCollection<long> sentClientSeqs)
    {
        return command.ServerSeq > lastServerSeq && !IsOwnCommand(command, myUserId, sentClientSeqs);
    }

    public static bool TargetsAbandonedSong(ProviderListenTogetherRemoteCommand command, string? abandonedSongId,
        long abandonedAtMilliseconds, long nowMilliseconds)
    {
        return !string.IsNullOrEmpty(abandonedSongId) && command.CommandId != ListenTogetherCommands.Goto &&
               command.TargetItemId == abandonedSongId &&
               nowMilliseconds - abandonedAtMilliseconds < AbandonedSongWindow.TotalMilliseconds;
    }

    public static bool IsStaleRoomPlayback(long knownVersion, long incomingVersion)
    {
        return incomingVersion > 0 && knownVersion > incomingVersion;
    }

    public static bool ShouldWaitForRoom(string? localSongId, string? roomNextSongId, TimeSpan roomDuration,
        TimeSpan roomRemaining)
    {
        return !string.IsNullOrEmpty(localSongId) && localSongId == roomNextSongId && roomDuration > TimeSpan.Zero &&
               roomRemaining < RoomHandoverWindow;
    }

    public static TimeSpan ExpectedPosition(ProviderListenTogetherRemoteCommand command, long serverNowMilliseconds)
    {
        if (!command.IsPlaying || command.ServerSeq < MinimumEpochMilliseconds || serverNowMilliseconds <= command.ServerSeq)
            return command.Position;
        var elapsed = TimeSpan.FromMilliseconds(serverNowMilliseconds - command.ServerSeq);
        return command.Position + (elapsed > MaximumExtrapolation ? MaximumExtrapolation : elapsed);
    }

    public static bool NeedsSeek(TimeSpan local, TimeSpan expected)
    {
        return (local - expected).Duration() >= DriftTolerance;
    }

    public static long? EstimateClockOffset(ProviderListenTogetherRemoteCommand ownCommand, long sentAtLocalMilliseconds)
    {
        if (ownCommand.ServerSeq < MinimumEpochMilliseconds || sentAtLocalMilliseconds <= 0) return null;
        var offset = ownCommand.ServerSeq - sentAtLocalMilliseconds;
        return Math.Abs(offset) <= TimeSpan.FromHours(1).TotalMilliseconds ? offset : null;
    }

    public static string QueueKey(IEnumerable<string> ids)
    {
        return string.Join(",", ids);
    }

    public static string VersionKey(IEnumerable<ProviderListenTogetherQueueVersion> versions)
    {
        return string.Join(";", versions.OrderBy(version => version.UserId, StringComparer.Ordinal)
            .Select(version => $"{version.UserId}:{version.Version}"));
    }
}
