using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Storage;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public interface IMediaSourceProvider
{
    string Id { get; }

    Task<MediaSource?> CreateAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct);
}

public interface IMediaPrefetchProvider
{
    Task PrefetchAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct);
}

public interface IMediaTransferProvider
{
    bool IsTransferring(SingleSongBase song);

    void CancelTransfersExcept(SingleSongBase keep);
}

public interface IMediaOfflineProvider
{
    Task<StorageFile?> FindOfflineFileAsync(SingleSongBase song);
}

public static class MediaSourceTraits
{
    public const string Progressive = "progressive";

    public static bool IsProgressive(MediaSource? source)
    {
        try
        {
            return source is not null
                   && source.CustomProperties.TryGetValue(Progressive, out var value)
                   && value is true;
        }
        catch
        {
            return false;
        }
    }
}

public interface IMediaSourceService
{
    Task PrefetchAsync(SingleSongBase song, CancellationToken ct);

    bool IsTransferring(SingleSongBase song);

    void CancelTransfersExcept(SingleSongBase keep);

    Task<MusicResourceBase?> ResolveAsync(SingleSongBase song, CancellationToken ct);

    Task<MediaSource?> CreateMediaSourceAsync(SingleSongBase song, MusicResourceBase resource, CancellationToken ct);
}
