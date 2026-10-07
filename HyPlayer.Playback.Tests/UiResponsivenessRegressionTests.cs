using HyPlayer.Features.Playback.Services;
using HyPlayer.LyricRenderer;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.Platform.Storage;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Microsoft.Graphics.Canvas;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class UiResponsivenessRegressionTests
{
    [Test]
    public async Task LyricReplacement_ShouldNotDisposeAFrameStillBeingDrawn()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = new TrackedLine(entered, release);
        var replacement = new TrackedLine();
        var renderer = new LyricRenderView();
        renderer.EnableRenderThreadUpdates();
        renderer.Redesign(100, 100, 96);
        renderer.SetLyricLines([old]);
        var draw = Task.Run(() => renderer.Draw(null!, default));
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Frame did not start.");
            renderer.SetLyricLines([replacement]);
            if (old.Disposed) throw new Exception("Replacing lyrics disposed a line used by an active frame.");
        }
        finally
        {
            release.Set();
            await draw;
        }
        renderer.Draw(null!, default);
        if (!old.Disposed || old.UsedAfterDispose || renderer.Context.LyricLines.Single() != replacement)
            throw new Exception("Replacement must be applied between frames.");
        renderer.ReleaseResources();
        renderer.SetLyricLines([new TrackedLine()]);
        renderer.Draw(null!, default);
        if (renderer.Context.LyricLines.Count != 0) throw new Exception("Stopped renderer accepted late lyrics.");
    }

    [Test]
    public async Task LyricClear_ShouldDeferResourceReleaseUntilFrameBoundary()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var line = new TrackedLine(entered, release);
        var renderer = new LyricRenderView();
        renderer.EnableRenderThreadUpdates();
        renderer.Redesign(100, 100, 96);
        renderer.SetLyricLines([line]);
        var draw = Task.Run(() => renderer.Draw(null!, default));
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new Exception("Frame did not start.");
            renderer.Clear();
            if (line.Disposed) throw new Exception("Clear disposed an active frame's resource.");
        }
        finally
        {
            release.Set();
            await draw;
        }
        renderer.Draw(null!, default);
        if (!line.Disposed || line.UsedAfterDispose) throw new Exception("Clear did not safely release the old line.");
        renderer.ReleaseResources();
    }

    [Test]
    public void ShuffledQueue_ShouldUseLinearIdentityLookup()
    {
        var queue = Enumerable.Range(0, 10000).Select(i => new CountingSong { ActualId = i.ToString() }).ToArray();
        var result = PlayCoreQueueSnapshot.Build(queue, queue.Reverse().ToArray());
        if (result.Length != queue.Length || result[0].QueueIndex != 9999 || result[^1].QueueIndex != 0)
            throw new Exception("Shuffled queue order/index changed.");
        if (queue.Sum(song => song.IdentityReads) > queue.Length * 4)
            throw new Exception("Shuffled queue performs more than a linear number of identity reads.");
    }

    [Test]
    public void ShuffledQueue_ShouldKeepFirstDuplicateAndSkipMissingSongs()
    {
        var first = new CountingSong { ActualId = "duplicate" };
        var second = new CountingSong { ActualId = "duplicate" };
        var other = new CountingSong { ActualId = "other" };
        var missing = new CountingSong { ActualId = "missing" };
        var result = PlayCoreQueueSnapshot.Build((SingleSongBase[])[first, second, other],
            (SingleSongBase[])[second, missing, other]);
        if (result.Length != 2 || result[0].ProviderItem != first || result[1].QueueIndex != 2)
            throw new Exception("Existing duplicate/missing-item semantics changed.");
    }

    [Test]
    public async Task FileScan_ShouldReadBoundedPagesAndPublishBeforeFetchingEverything()
    {
        var calls = 0;
        var read = 0;
        await foreach (var page in PagedBatchReader.ReadAsync<int>((offset, count, token) =>
        {
            calls++;
            if (count != 64) throw new Exception("Unbounded directory query.");
            IReadOnlyList<int> items = Enumerable.Range((int)offset, Math.Min((int)count, 10000 - (int)offset)).ToArray();
            return Task.FromResult(items);
        }, 64))
        {
            if (read == 0 && calls != 1) throw new Exception("Fetched the entire directory before publishing.");
            read += page.Count;
        }
        if (read != 10000 || calls != 158) throw new Exception("Paged scan lost/duplicated files.");
    }

    [Test]
    public async Task FileScan_ShouldDiscardResultsWhenCancelledDuringRead()
    {
        using var cancellation = new CancellationTokenSource();
        var published = 0;
        try
        {
            await foreach (var page in PagedBatchReader.ReadAsync<int>((offset, count, token) =>
            {
                cancellation.Cancel();
                return Task.FromResult<IReadOnlyList<int>>(new[] { 1 });
            }, 64, cancellation.Token)) published += page.Count;
            throw new Exception("Cancelled scan completed normally.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (published != 0) throw new Exception("Cancelled scan published results to a departed page.");
    }

    private sealed class CountingSong : SingleSongBase
    {
        public int IdentityReads;
        public override string ProviderId { get { IdentityReads++; return "test"; } }
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) =>
            Task.FromResult<List<PersonBase>?>(null);
    }

    private sealed class TrackedLine : RenderingLyricLine
    {
        private readonly ManualResetEventSlim? _entered;
        private readonly ManualResetEventSlim? _release;
        public volatile bool Disposed;
        public bool UsedAfterDispose;
        public TrackedLine(ManualResetEventSlim? entered = null, ManualResetEventSlim? release = null)
        {
            _entered = entered;
            _release = release;
            StartTime = GroupStartTime = 100;
            EndTime = GroupEndTime = 200;
            HiddenOnBlur = true;
        }
        public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
        {
            _entered?.Set();
            _release?.Wait();
            UsedAfterDispose |= Disposed;
        }
        protected override bool RenderCore(CanvasDrawingSession session, RenderContext context) => true;
        public override void Dispose() { Disposed = true; base.Dispose(); }
    }
}
