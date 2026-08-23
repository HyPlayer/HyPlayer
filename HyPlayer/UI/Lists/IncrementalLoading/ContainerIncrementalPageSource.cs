using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using ObservableCollections;

namespace HyPlayer.UI.Lists.IncrementalLoading;

public static class ContainerIncrementalPageSource
{
    public static IIncrementalSource<ProvidableItemBase>? Create(
        ContainerBase? container,
        int maximumPageSize)
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
        int maximumPageSize) : IIncrementalSource<ProvidableItemBase>
    {
        private int _offset;
        private bool _hasMore = true;

        public async Task<IEnumerable<ProvidableItemBase>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (!_hasMore)
                return [];

            var requestedCount = Math.Clamp(container.MaxProgressiveCount, 1, maximumPageSize);
            var (hasMore, items) = await container.GetProgressiveItemsListAsync(
                _offset, requestedCount, cancellationToken);
            items ??= [];
            _offset += items.Count;
            _hasMore = hasMore && items.Count > 0;
            return items;
        }
    }

    private sealed class UndeterminedSource(UndeterminedContainerBase container)
        : IIncrementalSource<ProvidableItemBase>
    {
        private bool _hasMore = true;

        public async Task<IEnumerable<ProvidableItemBase>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (!_hasMore)
                return [];

            var items = await container.GetNextItemsRangeAsync(cancellationToken) ?? [];
            _hasMore = items.Count > 0;
            return items;
        }
    }

    private sealed class LinerSource(LinerContainerBase container) : IIncrementalSource<ProvidableItemBase>
    {
        public async Task<IEnumerable<ProvidableItemBase>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            return pageIndex == 0
                ? await container.GetAllItemsAsync(cancellationToken) ?? []
                : [];
        }
    }
}

public sealed class MappingIncrementalSource<TSource, TTarget>(
    IIncrementalSource<TSource> source,
    Func<TSource, int, CancellationToken, Task<TTarget>> mapAsync) : IIncrementalSource<TTarget>
{
    private int _mappedCount;

    public async Task<IEnumerable<TTarget>> GetPagedItemsAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var page = await source.GetPagedItemsAsync(pageIndex, pageSize, cancellationToken);
        var mapped = new List<TTarget>();
        foreach (var item in page)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mapped.Add(await mapAsync(item, _mappedCount++, cancellationToken));
        }

        return mapped;
    }
}

/// <summary>
/// Keeps the CommunityToolkit collection instance stable while HyPlayer switches
/// the application-specific source behind it.
/// </summary>
public sealed partial class SwitchingIncrementalSource<T> : IIncrementalSource<T>, IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource _sourceCancellation = new();
    private IIncrementalSource<T>? _source;
    private long _generation;

    public void Reset(IIncrementalSource<T>? source)
    {
        lock (_gate)
        {
            _generation++;
            _sourceCancellation.Cancel();
            _sourceCancellation.Dispose();
            _sourceCancellation = new CancellationTokenSource();
            _source = source;
        }
    }

    public async Task<IEnumerable<T>> GetPagedItemsAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IIncrementalSource<T>? source;
        CancellationToken sourceCancellation;
        long generation;
        lock (_gate)
        {
            source = _source;
            sourceCancellation = _sourceCancellation.Token;
            generation = _generation;
        }

        if (source is null)
            return [];

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            sourceCancellation);
        var items = await source.GetPagedItemsAsync(pageIndex, pageSize, linkedCancellation.Token);
        linkedCancellation.Token.ThrowIfCancellationRequested();

        lock (_gate)
            return generation == _generation ? items : [];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _sourceCancellation.Cancel();
            _sourceCancellation.Dispose();
            _source = null;
        }
    }
}
