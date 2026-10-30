#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HyPlayer.Platform.Runtime.Background;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.UWP.Chopin.Dsp;
using Windows.Storage;

namespace HyPlayer.Features.Playback.AutoMix;

public static class AutoMixLog
{
    private const int MaxPendingLines = 256;
    private const int MaxLineLength = 2048;
    private static readonly Queue<string> Pending = new();
    private static readonly object Sync = new();
    private static readonly IBackgroundTaskRunner Runner = new BackgroundTaskRunner();
    private static Task _flushTask = Task.CompletedTask;
    private static CancellationTokenSource? _delayCancellation;
    private static bool _flushing;
    private static bool _stopped;

    public static void Write(string message)
    {
        lock (Sync)
        {
            if (_stopped) return;
            if (Pending.Count == MaxPendingLines) Pending.Dequeue();
            if (message.Length > MaxLineLength) message = message[..MaxLineLength];
            Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
            if (_flushing) return;
            _flushing = true;
            var cancellation = new CancellationTokenSource();
            _delayCancellation = cancellation;
            _flushTask = FlushAsync(cancellation);
            Runner.Forget(_flushTask, "write automix log");
        }
    }

    public static async Task StopAsync()
    {
        Task flushing;
        lock (Sync)
        {
            _stopped = true;
            _delayCancellation?.Cancel();
            flushing = _flushTask;
        }
        try { await flushing.ConfigureAwait(false); }
        catch { /* Failure is observed by Runner; logging never blocks shutdown with an exception. */ }
    }

    public static void Resume()
    {
        lock (Sync) _stopped = false;
    }

    public static void WriteSummary(AutoMixEngine engine, MixPumpStats stats) => Write(Describe(engine, stats));

    public static string Describe(AutoMixEngine engine, MixPumpStats stats)
    {
        var quantum = Math.Max(1, stats.QuantumFrames);
        var starvedQuanta = (stats.StarvedFrames + quantum - 1) / quantum;
        var starvedMs = stats.StarvedFrames * 1000.0 / Math.Max(1, stats.SampleRate);
        var handOver = engine.HandOverCrossfaded ? "hand-over crossfade" : "no hand-over";
        var handBack = engine.HandBackForced ? "hand-back forced"
            : engine.HandBackCrossfaded ? FormattableString.Invariant($"hand-back crossfade (measured {engine.HandBackMeasured:0} frames off, {engine.HandBackChases} chases)")
            : "no hand-back";
        var glide = string.Empty;
        if (engine.Plan.HasGlide && engine.GlideFrames <= 0)
        {
            glide = ", glide not run";
        }
        else if (engine.Plan.HasGlide)
        {
            var tempo = engine.Plan.TempoA;
            var span = tempo > 0 ? FormattableString.Invariant($"{tempo * engine.GlideFirstRateA:0.0}->{tempo * engine.GlideLastRateA:0.0} bpm ") : string.Empty;
            glide = FormattableString.Invariant(
                $", glide {span}over {engine.GlideSeconds:0.0}s (A x{engine.GlideFirstRateA:0.0000}->x{engine.GlideLastRateA:0.0000}, B ended x{engine.GlideLastRateB:0.0000}), max rate deviation {100 * engine.GlideDeviation:0.00}%");
        }

        return FormattableString.Invariant(
            $"mix thread: {engine.Stage}, {handOver}, {handBack}, starved {starvedQuanta} quanta ({starvedMs:0.0} ms), below low-water {stats.LowWaterWakes} of {stats.Wakes} wake-ups (empty {stats.EmptyWakes}), lead min {stats.MinLeadMs:0} ms / median {stats.MedianLeadMs:0} ms, longest wake gap {stats.MaxWakeGapMs:0.0} ms, audio-thread callbacks {stats.AudioThreadCallbacks}{(stats.Failed ? ", sink failed" : "")}{glide}");
    }

    private static async Task FlushAsync(CancellationTokenSource delayCancellation)
    {
        await Task.Yield();
        try
        {
            try { await Task.Delay(200, delayCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
            { /* Suspension expedites the final flush instead of losing pending messages. */ }
            var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.OpenIfExists);
            while (true)
            {
                string batch;
                lock (Sync)
                {
                    if (Pending.Count == 0)
                    {
                        _flushing = false;
                        return;
                    }
                    var lines = new System.Text.StringBuilder();
                    while (Pending.TryDequeue(out var line)) lines.AppendLine(line);
                    batch = lines.ToString();
                }
                if ((await file.GetBasicPropertiesAsync()).Size > 512 * 1024)
                    file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.ReplaceExisting);
                await FileIO.AppendTextAsync(file, batch);
            }
        }
        catch
        {
            // Diagnostics are best-effort: discard this bounded batch on storage failure.
            // Retry only after a future Write, never recursively spin on a failed disk.
            lock (Sync) Pending.Clear();
            throw;
        }
        finally
        {
            lock (Sync)
            {
                if (ReferenceEquals(_delayCancellation, delayCancellation))
                {
                    _delayCancellation = null;
                    _flushing = false;
                }
                delayCancellation.Dispose();
            }
        }
    }
}
