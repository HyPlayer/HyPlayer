using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace HyPlayer.Services.Playback.PlayCoreBridge;

public sealed class LegacyMediaSourceMusicResource : MusicResourceBase
{
    public required HyPlayItem LegacyItem { get; init; }

    public MediaSource? LegacyMediaSource { get; init; }

    public double? SuggestedVolume { get; init; }

    public override Task<ResourceResultBase> GetResourceAsync(
        ResourceQualityTag? qualityTag = null,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();

        return Task.FromResult<ResourceResultBase>(new LegacyMediaSourceMusicResourceResult
        {
            ExternalException = LegacyMediaSource is null
                ? new InvalidOperationException("Legacy media source was not created.")
                : null,
            ResourceStatus = LegacyMediaSource is null ? ResourceStatus.Fail : ResourceStatus.Success
        });
    }

    private sealed class LegacyMediaSourceMusicResourceResult : ResourceResultBase
    {
        public override required Exception? ExternalException { get; init; }

        public override required ResourceStatus ResourceStatus { get; init; }
    }
}
