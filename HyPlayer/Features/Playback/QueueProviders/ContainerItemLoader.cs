using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.Features.Playback.QueueProviders;

internal static class ContainerItemLoader
{
    public static async Task<List<ProvidableItemBase>> LoadAllAsync(ContainerBase container,
        CancellationToken cancellationToken)
    {
        return container switch
        {
            LinerContainerBase liner => await liner.GetAllItemsAsync(cancellationToken),
            IProgressiveLoadingContainer progressive => await LoadAllProgressiveItemsAsync(progressive,
                cancellationToken),
            UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(cancellationToken),
            _ => []
        };
    }

    private static async Task<List<ProvidableItemBase>> LoadAllProgressiveItemsAsync(
        IProgressiveLoadingContainer container,
        CancellationToken cancellationToken)
    {
        var items = new List<ProvidableItemBase>();
        foreach (var page in await LoadProgressivePagesAsync(container, container.MaxProgressiveCount,
                     cancellationToken))
            items.AddRange(page);

        return items;
    }

    public static async Task<List<List<ProvidableItemBase>>> LoadProgressivePagesAsync(
        IProgressiveLoadingContainer container,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var pages = new List<List<ProvidableItemBase>>();
        var offset = 0;
        var hasMore = true;

        while (hasMore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await container.GetProgressiveItemsListAsync(offset, pageSize, cancellationToken);
            hasMore = result.Item1;
            if (result.Item2 is not { Count: > 0 } page)
                break;

            pages.Add(page);
            offset += page.Count;
        }

        return pages;
    }
}