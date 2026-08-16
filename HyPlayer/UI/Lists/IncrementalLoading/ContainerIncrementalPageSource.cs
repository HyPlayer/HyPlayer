using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;

namespace HyPlayer.UI.Lists.IncrementalLoading;

public static class ContainerIncrementalPageSource
{
    public static IIncrementalPageSource<ProvidableItemBase>? Create(ContainerBase? container, int maximumPageSize)
    {
        return container switch
        {
            IProgressiveLoadingContainer progressive => new ProgressiveSource(progressive, maximumPageSize),
            UndeterminedContainerBase undetermined => new UndeterminedSource(undetermined),
            LinerContainerBase liner => new LinerSource(liner),
            _ => null
        };
    }

    private sealed class ProgressiveSource(
        IProgressiveLoadingContainer container,
        int maximumPageSize) : IIncrementalPageSource<ProvidableItemBase>
    {
        private int _offset;

        public bool HasMore { get; private set; } = true;

        public async Task<IncrementalPage<ProvidableItemBase>> LoadNextAsync(
            int desiredCount,
            CancellationToken cancellationToken)
        {
            var pageSize = Math.Clamp(container.MaxProgressiveCount, 1, maximumPageSize);
            var (hasMore, items) = await container.GetProgressiveItemsListAsync(
                _offset, pageSize, cancellationToken);
            items ??= [];
            _offset += items.Count;
            HasMore = hasMore && items.Count > 0;
            return new IncrementalPage<ProvidableItemBase>(items, HasMore);
        }
    }

    private sealed class UndeterminedSource(UndeterminedContainerBase container)
        : IIncrementalPageSource<ProvidableItemBase>
    {
        public bool HasMore { get; private set; } = true;

        public async Task<IncrementalPage<ProvidableItemBase>> LoadNextAsync(
            int desiredCount,
            CancellationToken cancellationToken)
        {
            var items = await container.GetNextItemsRangeAsync(cancellationToken) ?? [];
            HasMore = items.Count > 0;
            return new IncrementalPage<ProvidableItemBase>(items, HasMore);
        }
    }

    private sealed class LinerSource(LinerContainerBase container) : IIncrementalPageSource<ProvidableItemBase>
    {
        public bool HasMore { get; private set; } = true;

        public async Task<IncrementalPage<ProvidableItemBase>> LoadNextAsync(
            int desiredCount,
            CancellationToken cancellationToken)
        {
            if (!HasMore)
                return new IncrementalPage<ProvidableItemBase>([], false);

            var items = await container.GetAllItemsAsync(cancellationToken) ?? [];
            HasMore = false;
            return new IncrementalPage<ProvidableItemBase>(items, false);
        }
    }
}

public sealed class MappingIncrementalPageSource<TSource, TTarget>(
    IIncrementalPageSource<TSource> source,
    Func<TSource, int, CancellationToken, Task<TTarget>> mapAsync) : IIncrementalPageSource<TTarget>
{
    private int _mappedCount;

    public bool HasMore => source.HasMore;

    public async Task<IncrementalPage<TTarget>> LoadNextAsync(
        int desiredCount,
        CancellationToken cancellationToken)
    {
        var page = await source.LoadNextAsync(desiredCount, cancellationToken);
        var mapped = new List<TTarget>(page.Items.Count);
        foreach (var item in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mapped.Add(await mapAsync(item, _mappedCount++, cancellationToken));
        }

        return new IncrementalPage<TTarget>(mapped, page.HasMore);
    }
}
