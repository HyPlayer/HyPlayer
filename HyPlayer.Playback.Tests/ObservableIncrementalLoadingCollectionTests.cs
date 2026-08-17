using ObservableCollections;
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
