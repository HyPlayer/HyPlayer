using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class NeteaseStreamingProvider : IMediaSourceProvider
{
    public const string ProviderId = "nst";

    public string Id => ProviderId;

    public Task<MediaSource?> CreateAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(resource.Uri is null ? null : MediaSource.CreateFromUri(resource.Uri));
    }
}
