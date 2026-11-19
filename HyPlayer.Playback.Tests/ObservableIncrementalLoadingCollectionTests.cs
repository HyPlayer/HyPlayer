using ObservableCollections;
using System.Collections.Specialized;
using HyPlayer.UI.Lists.IncrementalLoading;
using HyPlayer.Application.Loading;
using HyPlayer.UI.Lists;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class ObservableIncrementalLoadingCollectionTests
{
    [Test]
    public async Task Loads_pages_with_the_community_toolkit_contract()
    {
        var source = new QueueSource(
            new[] { 1, 2 },
            new[] { 3 },
            []);
        var starts = 0;
        var ends = 0;
        var collection = new IncrementalLoadingCollection<QueueSource, int>(
            source,
            itemsPerPage: 2,
            onStartLoading: () => starts++,
            onEndLoading: () => ends++);

        var first = await collection.LoadMoreItemsAsync(99);
        var second = await collection.LoadMoreItemsAsync(99);
        var exhausted = await collection.LoadMoreItemsAsync(99);

        Check(first.Count == 2, "The first load should report both appended items.");
        Check(second.Count == 1, "The second load should report its appended item.");
        Check(exhausted.Count == 0, "An empty page should report no appended items.");
        Check(collection.SequenceEqual(new[] { 1, 2, 3 }), "Pages should be appended in order.");
        Check(!collection.HasMoreItems, "An empty page should exhaust the collection.");
        Check(source.Requests.SequenceEqual(new[] { (0, 2), (1, 2), (2, 2) }),
            "The source should receive zero-based page indexes and the configured page size.");
        Check(starts == 3 && ends == 3, "Start and end callbacks should bracket every request.");
    }

    [Test]
    public async Task A_handled_source_error_uses_the_toolkit_error_callback()
    {
        Exception? observed = null;
        var collection = new IncrementalLoadingCollection<FailingSource, int>(
            new FailingSource(),
            onError: exception => observed = exception);

        var result = await collection.LoadMoreItemsAsync(1);

        Check(result.Count == 0, "A handled failure should not append items.");
        Check(observed is InvalidOperationException, "The source exception should reach OnError.");
        Check(!collection.HasMoreItems, "A handled failure follows the toolkit empty-result behavior.");
    }

    [Test]
    public async Task Retry_preserves_loaded_rows_and_repeats_the_failed_page()
    {
        var requests = new List<int>();
        var fail = true;
        var source = new DelegateSource(page =>
        {
            requests.Add(page);
            if (page == 1 && fail)
            {
                fail = false;
                throw new InvalidOperationException("Injected failure.");
            }
            return Task.FromResult<IEnumerable<int>>(page < 2 ? new[] { page + 1 } : []);
        });
        var collection = new IncrementalLoadingCollection<DelegateSource, int>(source, onError: _ => { });
        using var view = collection.ToNotifyCollectionChanged();
        var resets = 0;
        view.CollectionChanged += (_, args) => resets += args.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

        await collection.LoadMoreItemsAsync(20);
        await collection.LoadMoreItemsAsync(20);
        Check(!collection.HasMoreItems && collection.SequenceEqual(new[] { 1 }), "Failure should retain rows and pause automatic loading.");
        await collection.LoadMoreItemsAsync(20);
        Check(requests.SequenceEqual(new[] { 0, 1 }), "Automatic loading retried a failed request.");
        var retried = await collection.RetryAsync();
        await collection.LoadMoreItemsAsync(20);
        await collection.RetryAsync();

        Check(retried.Count == 1 && collection.SequenceEqual(new[] { 1, 2 }), "Retry discarded or duplicated rows.");
        Check(requests.SequenceEqual(new[] { 0, 1, 1, 2 }), "Retry must repeat only a failed page, not an exhausted page.");
        Check(resets == 0, "Retry must not reset the bound view.");
    }

    [Test]
    public async Task Mapping_failure_reuses_the_fetched_page_and_commits_indices_only_on_success()
    {
        var fetches = 0;
        var fail = true;
        var source = new DelegateSource(_ =>
        {
            fetches++;
            return Task.FromResult<IEnumerable<int>>(new[] { 10, 20 });
        });
        var mapped = new MappingIncrementalSource<int, int>(source, (item, index, _) =>
        {
            if (item == 20 && fail) { fail = false; throw new InvalidOperationException("Mapping failed."); }
            return Task.FromResult(index);
        });
        var collection = new IncrementalLoadingCollection<MappingIncrementalSource<int, int>, int>(mapped, onError: _ => { });

        await collection.LoadMoreItemsAsync(20);
        Check(collection.Count == 0, "A partially mapped page was published.");
        await collection.RetryAsync();
        Check(fetches == 1 && collection.SequenceEqual(new[] { 0, 1 }), "Retry refetched a consumed page or advanced row indices.");
        await collection.LoadMoreItemsAsync(20);
        Check(collection.SequenceEqual(new[] { 0, 1, 2, 3 }), "Next-page indices did not follow the committed page.");
    }

    [Test]
    public async Task A_page_is_enumerated_once_and_exhausted_loads_emit_no_reset()
    {
        var enumerations = 0;
        IEnumerable<int> Page()
        {
            if (++enumerations != 1) throw new InvalidOperationException("Page enumerated twice.");
            yield return 1;
            yield return 2;
        }
        var source = new DelegateSource(page => Task.FromResult(page == 0 ? Page() : []));
        var collection = new IncrementalLoadingCollection<DelegateSource, int>(source);
        using var view = collection.ToNotifyCollectionChanged();
        var actions = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, args) => actions.Add(args.Action);
        await collection.LoadMoreItemsAsync(20);
        await collection.LoadMoreItemsAsync(20);
        await collection.LoadMoreItemsAsync(20);
        Check(enumerations == 1 && actions.SequenceEqual(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add }),
            "Pages must be materialized once and only appended.");
    }

    private sealed class DelegateSource(Func<int, Task<IEnumerable<int>>> load) : IIncrementalSource<int>
    {
        public Task<IEnumerable<int>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default) => load(pageIndex);
    }

    [Test]
    public async Task First_page_retry_does_not_require_recreating_the_source()
    {
        var calls = 0;
        var source = new DelegateSource(page =>
        {
            Check(page == 0, "A failed first page advanced its index.");
            if (++calls == 1) throw new InvalidOperationException("First page failed.");
            return Task.FromResult<IEnumerable<int>>(new[] { 7 });
        });
        var collection = new IncrementalLoadingCollection<DelegateSource, int>(source, onError: _ => { });
        await collection.LoadMoreItemsAsync(20);
        await collection.RetryAsync();
        Check(collection.SequenceEqual(new[] { 7 }) && calls == 2, "First-page retry failed.");
    }

    [Test]
    public async Task Switching_source_during_load_discards_the_old_page_and_loads_the_new_first_page()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<IEnumerable<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new SwitchingIncrementalSource<int>();
        source.Reset(new DelegateSource(_ => { started.SetResult(); return reply.Task; }));
        var collection = new IncrementalLoadingCollection<SwitchingIncrementalSource<int>, int>(source);
        var pending = collection.LoadMoreItemsAsync(20).AsTask();
        await started.Task;
        source.Reset(new DelegateSource(page =>
        {
            Check(page == 0, "A new source inherited the old page index.");
            return Task.FromResult<IEnumerable<int>>(new[] { 9 });
        }));
        collection.Clear();
        await collection.RefreshAsync();
        reply.SetResult(new[] { 1 });
        await pending;
        Check(collection.SequenceEqual(new[] { 9 }), "An old request leaked into the new source.");
    }

    [Test]
    public async Task Swallowed_provider_failure_does_not_commit_an_exhausted_cursor()
    {
        var calls = 0;
        var container = new DelegateProgressiveContainer((offset, count, token) =>
        {
            Check(offset == 0, "A failed request advanced the offset.");
            if (++calls == 1)
            {
                LoadRequestProbe.Current?.ReportFailure(new InvalidOperationException("Swallowed provider error."));
                return Task.FromResult<(bool, List<ProvidableItemBase>)>((false, []));
            }
            return Task.FromResult<(bool, List<ProvidableItemBase>)>((false, [new StaticItemsContainer([])]));
        }, "test", "test", "test");
        var source = ContainerIncrementalPageSource.Create(container, 20)!;
        var collection = new IncrementalLoadingCollection<IIncrementalSource<ProvidableItemBase>, ProvidableItemBase>(source, onError: _ => { });
        await collection.LoadMoreItemsAsync(20);
        Check(!collection.HasMoreItems && collection.Count == 0, "Provider failure did not pause loading.");
        await collection.RetryAsync();
        Check(calls == 2 && collection.Count == 1, "The swallowed error permanently exhausted the source.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class QueueSource(params int[][] pages) : IIncrementalSource<int>
    {
        private readonly Queue<int[]> _pages = new(pages);

        public List<(int PageIndex, int PageSize)> Requests { get; } = [];

        public Task<IEnumerable<int>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((pageIndex, pageSize));
            return Task.FromResult<IEnumerable<int>>(_pages.Dequeue());
        }
    }

    private sealed class FailingSource : IIncrementalSource<int>
    {
        public Task<IEnumerable<int>> GetPagedItemsAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Injected failure.");
        }
    }
}
