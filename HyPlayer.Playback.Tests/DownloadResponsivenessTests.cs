using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class DownloadResponsivenessTests
{
    [Test]
    public void Queue_ShouldAccountForActiveDownloadsAfterQueuedItems()
    {
        using var dispatcher = new BlockingDispatcher();
        var queued = CreateDownload(dispatcher);
        var active = CreateDownload(dispatcher);
        active.Status = DownloadObject.DownloadStatus.Downloading;
        if (DownloadQueueScheduler.GetReadyDownloads((DownloadObject[])[queued, active], 1).Length != 0)
            throw new InvalidOperationException("An active download later in the queue must occupy a slot.");
    }

    [Test]
    public void Queue_ShouldFillAvailableSlotsAndBoundFlacPostprocessing()
    {
        using var dispatcher = new BlockingDispatcher();
        var first = CreateDownload(dispatcher);
        var second = CreateDownload(dispatcher);
        var third = CreateDownload(dispatcher);
        var writing = CreateDownload(dispatcher);
        writing.Status = DownloadObject.DownloadStatus.Processing;
        if (!DownloadQueueScheduler.GetReadyDownloads((DownloadObject[])[first, second, third, writing], 3)
                .SequenceEqual((DownloadObject[])[first, second]))
            throw new InvalidOperationException("Fill two slots in order while the tag writer occupies the third.");
    }

    private static DownloadObject CreateDownload(IUIThreadDispatcher dispatcher) =>
        new(new TestSong(), new SilentNotification(), dispatcher,
            new DownloadSettings(), new LyricSettings(), null!, null!, [], null!,
            new DiagnosticsStateService());

    [Test]
    public async Task StartDownload_ShouldReturnToCallerWhilePreparationIsBlocked()
    {
        using var dispatcher = new BlockingDispatcher();
        var download = CreateDownload(dispatcher);
        Task? work = null;
        using var returned = new ManualResetEventSlim();
        var caller = Task.Run(() =>
        {
            work = download.StartDownload();
            returned.Set();
        });
        try
        {
            if (!dispatcher.Entered.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("Download preparation did not start.");
            if (!returned.Wait(TimeSpan.FromSeconds(2)))
                throw new InvalidOperationException("Download preparation blocked its caller/UI thread.");
            if (download.Status != DownloadObject.DownloadStatus.Downloading)
                throw new InvalidOperationException("The scheduler slot must be reserved before starting the worker.");
        }
        finally
        {
            download.Remove();
            dispatcher.Release.Set();
            await caller;
            if (work is not null) await work;
        }
    }

    private sealed class TestSong : SingleSongBase
    {
        public override string ProviderId => "test";
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) =>
            Task.FromResult<List<PersonBase>?>(null);
    }

    private sealed class SilentNotification : INotificationService
    {
        public void ShowMessage(string title, string? message = null) { }
    }

    private sealed class BlockingDispatcher : IUIThreadDispatcher, IDisposable
    {
        private int _calls;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public Task<bool> TryRunAsync(Action action)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.Set();
                Release.Wait();
            }
            return Task.FromResult(false);
        }

        public Task<bool> TryRunAsync(Func<Task> action) => Task.FromResult(false);

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
