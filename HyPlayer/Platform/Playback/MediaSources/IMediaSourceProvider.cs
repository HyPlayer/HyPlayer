using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public interface IMediaSourceProvider
{
    string Id { get; }

    Task<MediaSource?> CreateAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct);
}

public interface IMediaSourceService
{
    Task<MusicResourceBase?> ResolveAsync(SingleSongBase song, CancellationToken ct);

    Task<MediaSource?> CreateMediaSourceAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct);
}
