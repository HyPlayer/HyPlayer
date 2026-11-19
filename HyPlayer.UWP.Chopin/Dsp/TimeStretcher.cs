using System;
using HyPlayer.UWP.Chopin.Dsp.Signalsmith;

namespace HyPlayer.UWP.Chopin.Dsp;

/// <summary>Managed Signalsmith time stretching and pitch shifting of interleaved PCM.</summary>
public sealed class TimeStretcher : IDisposable
{
    internal const double DefaultBlockSeconds = 0.12;
    internal const double DefaultIntervalSeconds = 0.03;
    internal const double CompactBlockSeconds = 0.07;
    internal const double CompactIntervalSeconds = 0.0175;
    private SignalsmithStretch _stretch;
    private bool _disposed;
    // Existing capability checks remain valid without a separately deployed DLL.
    /// <summary>Whether the managed stretch backend is present; always true.</summary>
    public static bool IsAvailable => true;
    /// <summary>Whether pitch shifting is supported; always true.</summary>
    public static bool CanTranspose => true;

    /// <summary>Creates a processor with the upstream 120 ms block and 30 ms interval.</summary>
    /// <param name="channels">Number of interleaved PCM channels.</param>
    /// <param name="sampleRate">PCM sample rate in Hz.</param>
    public TimeStretcher(int channels, int sampleRate) : this(channels, sampleRate, DefaultBlockSeconds, DefaultIntervalSeconds) { }

    internal static (int Input, int Output) Latencies(int sampleRate, bool compact) =>
        StretchStft.Latencies((int)(sampleRate * (compact ? CompactBlockSeconds : DefaultBlockSeconds)),
            (int)(sampleRate * (compact ? CompactIntervalSeconds : DefaultIntervalSeconds)));

    /// <summary>Creates a processor and preallocates its transform and history buffers.</summary>
    /// <param name="channels">Number of interleaved PCM channels.</param>
    /// <param name="sampleRate">PCM sample rate in Hz.</param>
    /// <param name="blockSeconds">Analysis block length in seconds.</param>
    /// <param name="intervalSeconds">Spectrum processing interval in seconds.</param>
    public TimeStretcher(int channels, int sampleRate, double blockSeconds, double intervalSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!double.IsFinite(blockSeconds) || blockSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(blockSeconds));
        if (!double.IsFinite(intervalSeconds) || intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        var block = checked((int)(sampleRate * blockSeconds));
        var interval = checked((int)(sampleRate * intervalSeconds));
        if (block < 4 || interval < 1 || interval > block) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        _stretch = new SignalsmithStretch(channels, block, interval);
    }

    /// <summary>Returns input frames required to pre-roll output at the requested playback rate.</summary>
    /// <param name="rate">Input frames per output frame; zero queries input latency alone.</param>
    /// <returns>The required frame count, including analysis and synthesis latency.</returns>
    public int OutputSeekLength(double rate)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(rate) || rate < 0) throw new ArgumentOutOfRangeException(nameof(rate));
        return checked((int)(_stretch.InputLatency + (float)rate * _stretch.OutputLatency));
    }
    /// <summary>Resets and pre-rolls the supplied PCM so subsequent output starts at its beginning.</summary>
    /// <param name="input">Interleaved pre-roll PCM; normally OutputSeekLength frames.</param>
    public void OutputSeek(ReadOnlySpan<float> input) { ThrowIfDisposed(); _stretch.OutputSeek(input); }
    /// <summary>Processes disjoint interleaved buffers without allocating; their frame ratio sets speed.</summary>
    /// <param name="input">Input PCM containing complete frames.</param>
    /// <param name="output">Output PCM buffer containing complete frames.</param>
    public void Process(ReadOnlySpan<float> input, Span<float> output) { ThrowIfDisposed(); _stretch.Process(input, output); }
    /// <summary>Clears audio history and phase state, preserving transpose configuration.</summary>
    public void Reset() { ThrowIfDisposed(); _stretch.Reset(); }
    /// <summary>Sets the frequency multiplier; first non-unity use reserves pitch workspace before processing.</summary>
    /// <param name="factor">Positive frequency multiplier; one preserves pitch.</param>
    public void SetTranspose(double factor)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(factor) || factor <= 0 || factor > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(factor));
        _stretch.Transpose = (float)factor;
    }
    /// <summary>Marks this worker-owned instance unusable; all storage is managed.</summary>
    public void Dispose()
    {
        _disposed = true;
        _stretch = null; // Release large managed history/FFT buffers even if the wrapper is retained.
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
