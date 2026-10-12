using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.UWP.Chopin.Dsp;
using Windows.Storage;

namespace HyPlayer.Features.Playback.AutoMix;

public static class AutoMixLog
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static int _flushing;

    public static void Write(string message)
    {
        Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
        if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
        _ = Task.Run(FlushAsync);
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

    private static async Task FlushAsync()
    {
        try
        {
            await Task.Delay(200);
            var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.OpenIfExists);
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 512 * 1024)
                file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.ReplaceExisting);
            var lines = new System.Text.StringBuilder();
            while (Pending.TryDequeue(out var line)) lines.AppendLine(line);
            if (lines.Length > 0) await FileIO.AppendTextAsync(file, lines.ToString());
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
            if (!Pending.IsEmpty && Interlocked.Exchange(ref _flushing, 1) == 0) _ = Task.Run(FlushAsync);
        }
    }
}
