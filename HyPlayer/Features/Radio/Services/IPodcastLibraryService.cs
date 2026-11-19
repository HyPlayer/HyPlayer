using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Models;

namespace HyPlayer.Features.Radio.Services;

public static class PodcastEpisodeKinds
{
    public const string Liked = "liked";
    public const string Collected = "collected";
}

public interface IPodcastLibraryService
{
    ContainerBase CreateEpisodesContainer(string kind);

    Task<IReadOnlyList<ContainerBase>> GetCreatedChannelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerBase>> GetSubscribedChannelsAsync(CancellationToken cancellationToken = default);
}
