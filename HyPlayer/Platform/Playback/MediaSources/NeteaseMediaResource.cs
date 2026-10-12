using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class NeteaseMediaResource : NeteaseMusicResource, IChopinPlaybackSourceResource
{
    private readonly IMediaSourceService _service;

    public NeteaseMediaResource(SingleSongBase song, IMediaSourceService service)
    {
        Song = song ?? throw new ArgumentNullException(nameof(song));
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public SingleSongBase Song { get; }

    public double? SuggestedVolume => null;

    public async Task<AudioGraphPlaybackSource?> CreatePlaybackSourceAsync(CancellationToken ctk = default)
    {
        var mediaSource = await _service.CreateMediaSourceAsync(Song, this, ctk).ConfigureAwait(false);
        return mediaSource is null ? null : new AudioGraphPlaybackSource(mediaSource);
    }
}
