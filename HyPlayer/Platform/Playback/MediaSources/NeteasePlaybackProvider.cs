using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public sealed class NeteasePlaybackProvider : NeteaseProvider.NeteaseProvider, IMusicResourceProvidable
{
    Task<MusicResourceBase?> IMusicResourceProvidable.GetMusicResourceAsync(
        SingleSongBase song,
        ResourceQualityTag? qualityTag,
        CancellationToken ctk)
    {
        return qualityTag is null
            ? AppDepository.Resolve<IMediaSourceService>().ResolveAsync(song, ctk)
            : GetMusicResourceAsync(song, qualityTag, ctk);
    }
}
