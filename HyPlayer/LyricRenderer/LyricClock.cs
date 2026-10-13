using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HyPlayer.LyricRenderer;

public sealed class LyricClock
{
    internal const double MaxPhaseCorrection = 0.004;
    internal const double MaxFrequencyCorrection = 0.01;
    internal const double ForwardJumpMs = 80;
    internal const double BackwardJumpMs = 30;
    internal const double StallMs = 100;
    private const double FilterMs = 400;
    private const double PhaseGainPerMs = 0.0004;
    private const double FrequencyGainPerMs2 = 2.8e-8;
    private const double MaxIntegrationStepMs = 100;

    private bool _hasAnchor;
    private bool _playing;
    private bool _stalled;
    private int _sourceId;
    private double _outputMs;
    private double _sourceMs;
    private long _sourceTimestamp;
    private long _lastTimestamp;
    private double _filteredError;
    private double _frequency;

    internal double Frequency => _frequency;

    public long Next(object source, double positionMs, bool isPlaying, double speed, bool seek = false) =>
        (long)NextPrecise(source, positionMs, isPlaying, speed, seek);

    public double NextPrecise(object source, double positionMs, bool isPlaying, double speed, bool seek = false) =>
        NextPrecise(RuntimeHelpers.GetHashCode(source), positionMs, isPlaying, speed, seek, Stopwatch.GetTimestamp());

    internal double NextPrecise(int sourceId, double positionMs, bool isPlaying, double speed, bool seek,
        long timestamp)
    {
        if (!_hasAnchor || seek || sourceId != _sourceId || IsSourceJump(positionMs, speed, timestamp))
            return Anchor(sourceId, positionMs, isPlaying, timestamp);

        var elapsedMs = Math.Max(0, Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalMilliseconds);
        _lastTimestamp = timestamp;
        if (positionMs != _sourceMs)
        {
            _sourceMs = positionMs;
            _sourceTimestamp = timestamp;
        }

        if (!isPlaying)
        {
            if (_playing) _outputMs = Math.Max(_outputMs, positionMs);
            _playing = false;
            return _outputMs;
        }

        if (!_playing)
        {
            _playing = true;
            _sourceTimestamp = timestamp;
            return Restart(positionMs);
        }

        if (Stopwatch.GetElapsedTime(_sourceTimestamp, timestamp).TotalMilliseconds > StallMs)
        {
            _stalled = true;
            return _outputMs;
        }

        if (_stalled)
        {
            if (positionMs < _outputMs) return _outputMs;
            _stalled = false;
            return Restart(positionMs);
        }

        var correction = Math.Clamp(_filteredError * PhaseGainPerMs, -MaxPhaseCorrection, MaxPhaseCorrection);
        var predicted = _outputMs + elapsedMs * speed * (1 + _frequency + correction);
        var step = Math.Min(elapsedMs, MaxIntegrationStepMs);
        _filteredError += (positionMs - predicted - _filteredError) * (1 - Math.Exp(-step / FilterMs));
        _frequency = Math.Clamp(_frequency + _filteredError * FrequencyGainPerMs2 * step,
            -MaxFrequencyCorrection, MaxFrequencyCorrection);
        _outputMs = Math.Max(_outputMs, predicted);
        return _outputMs;
    }

    private bool IsSourceJump(double positionMs, double speed, long timestamp)
    {
        if (positionMs < _sourceMs - BackwardJumpMs) return true;
        var allowance = _playing
            ? Stopwatch.GetElapsedTime(_sourceTimestamp, timestamp).TotalMilliseconds * Math.Max(speed, 0) *
              (1 + MaxFrequencyCorrection)
            : 0;
        return positionMs > _sourceMs + allowance + ForwardJumpMs;
    }

    private double Restart(double positionMs)
    {
        _outputMs = Math.Max(_outputMs, positionMs);
        _filteredError = 0;
        return _outputMs;
    }

    private double Anchor(int sourceId, double positionMs, bool isPlaying, long timestamp)
    {
        _hasAnchor = true;
        _playing = isPlaying;
        _stalled = false;
        _sourceId = sourceId;
        _outputMs = positionMs;
        _sourceMs = positionMs;
        _sourceTimestamp = timestamp;
        _lastTimestamp = timestamp;
        _filteredError = 0;
        return positionMs;
    }
}
