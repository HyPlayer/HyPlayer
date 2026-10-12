using System;
using System.Diagnostics;

namespace HyPlayer.LyricRenderer;

public sealed class LyricClock
{
    private const double SnapThresholdMs = 120;
    private const double RateGainPerMs = 0.01;
    private const double MaxRateAdjust = 0.2;

    private bool _hasAnchor;
    private double _outputMs;
    private long _lastTimestamp;

    public long Next(double positionMs, bool isPlaying, double speed)
    {
        var now = Stopwatch.GetTimestamp();
        if (!isPlaying || !_hasAnchor)
            return Reset(positionMs, now, isPlaying);

        var elapsedMs = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
        _lastTimestamp = now;

        var drift = positionMs - _outputMs;
        if (Math.Abs(drift) > SnapThresholdMs)
            return Reset(positionMs, now, true);

        var rate = 1 + Math.Clamp(drift * RateGainPerMs, -MaxRateAdjust, MaxRateAdjust);
        _outputMs += elapsedMs * speed * rate;
        return (long)_outputMs;
    }

    private long Reset(double positionMs, long now, bool keepAnchor)
    {
        _hasAnchor = keepAnchor;
        _outputMs = positionMs;
        _lastTimestamp = now;
        return (long)positionMs;
    }
}
