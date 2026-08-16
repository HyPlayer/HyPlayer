using HyPlayer.UI.Lists.IncrementalLoading;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class IncrementalLoadControllerTests
{
    [Test]
    public async Task Loads_pages_sequentially_and_reaches_exhausted_state()
    {
        using var controller = new IncrementalLoadController<int>();
        controller.Reset(new QueuePageSource(
            new IncrementalPage<int>(new[] { 1, 2 }, true),
            new IncrementalPage<int>(new[] { 3 }, false)));

        var first = await controller.LoadNextAsync(2, CancellationToken.None);
        Check(first.SequenceEqual(new[] { 1, 2 }), "The first page should be returned unchanged.");
        Check(controller.Status == IncrementalLoadStatus.Idle, "A source with more items should return to Idle.");

        var second = await controller.LoadNextAsync(2, CancellationToken.None);
        Check(second.SequenceEqual(new[] { 3 }), "The second page should be returned unchanged.");
        Check(controller.Status == IncrementalLoadStatus.Exhausted, "The final page should exhaust the source.");
        Check(!controller.HasMore, "HasMore should reflect the source after the final page.");
    }

    [Test]
    public async Task Failed_load_can_be_retried_without_losing_source_position()
    {
        using var controller = new IncrementalLoadController<int>();
        controller.Reset(new FailOncePageSource());

        try
        {
            await controller.LoadNextAsync(1, CancellationToken.None);
            throw new InvalidOperationException("The first load was expected to fail.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "Injected failure.")
        {
        }

        Check(controller.Status == IncrementalLoadStatus.Failed, "A provider failure should enter Failed state.");
        Check(controller.CanRetry, "A failed source with more data should expose retry.");

        controller.PrepareRetry();
        var retry = await controller.LoadNextAsync(1, CancellationToken.None);
        Check(retry.SequenceEqual(new[] { 42 }), "Retry should load the page that previously failed.");
        Check(controller.Status == IncrementalLoadStatus.Exhausted, "Successful retry should update final state.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class QueuePageSource(params IncrementalPage<int>[] pages) : IIncrementalPageSource<int>
    {
        private readonly Queue<IncrementalPage<int>> _pages = new(pages);

        public bool HasMore { get; private set; } = pages.Length > 0;

        public Task<IncrementalPage<int>> LoadNextAsync(int desiredCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = _pages.Dequeue();
            HasMore = page.HasMore;
            return Task.FromResult(page);
        }
    }

    private sealed class FailOncePageSource : IIncrementalPageSource<int>
    {
        private bool _hasFailed;

        public bool HasMore { get; private set; } = true;

        public Task<IncrementalPage<int>> LoadNextAsync(int desiredCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new InvalidOperationException("Injected failure.");
            }

            HasMore = false;
            return Task.FromResult(new IncrementalPage<int>(new[] { 42 }, false));
        }
    }
}
