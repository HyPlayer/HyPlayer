using System;
using System.Diagnostics;
using System.Threading;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

public enum CanvasPacingMode
{
    Calibrating,
    Variable,
    Capped
}

public readonly record struct CanvasFrameTime(long TotalTicks, long ElapsedTicks, long FrameCount);

public readonly record struct CanvasLoopSchedule(bool IsFixedTimeStep, long TargetElapsedTicks, bool ResetElapsedTime);

public sealed class CanvasFramePacer
{
    private const int CalibrationFrames = 24;
    private const int HistoryLength = 16;
    private const int LongestMultiple = 8;
    private const double MatchTolerance = 0.2;
    private const double WideMatchTolerance = 0.4;
    private const double JitterToleranceMilliseconds = 1.2;
    private const double PeriodGain = 0.02;
    private const double DriftGain = 0.02;
    private const double LateAllowance = 0.15;
    private const double FasterRateBias = 0.05;
    private const double ShortestTarget = 0.25;
    private const double MaximumStepMilliseconds = 100;
    private const double ShortestPeriodMilliseconds = 2;
    private const double LongestPeriodMilliseconds = 50;
    private const byte Single = 1;
    private const byte Several = 2;
    private const byte Mismatch = 3;

    private readonly double _millisecondsPerTimestamp;
    private readonly double[] _calibration = new double[CalibrationFrames];
    private readonly double[] _sorted = new double[CalibrationFrames];
    private readonly byte[] _history = new byte[HistoryLength];
    private readonly object _configurationGate = new();
    private int _configurationVersion;
    private int _appliedConfigurationVersion;
    private bool _requestedCap;
    private double _requestedTargetMilliseconds;
    private bool _cap;
    private double _targetMilliseconds;
    private int _suspended;
    private int _calibrationCount;
    private int _historyIndex;
    private int _historyCount;
    private int _mismatches;
    private int _singles;
    private double _period;
    private int _interval = 1;
    private CanvasPacingMode _mode = CanvasPacingMode.Calibrating;
    private bool _updatePending;
    private long _updateTimestamp;
    private long _frameTimestamp;
    private long _frameStart;
    private bool _frameUpdated;
    private double _totalMilliseconds;
    private double _realMilliseconds;
    private long _totalTicks;
    private long _frameCount;
    private long _targetTicks;

    public CanvasFramePacer() : this(Stopwatch.Frequency)
    {
    }

    public CanvasFramePacer(long timestampFrequency)
    {
        _millisecondsPerTimestamp = 1000.0 / timestampFrequency;
    }

    public CanvasPacingMode Mode => _mode;

    public double RefreshPeriodMilliseconds => _period;

    public int Interval => _mode == CanvasPacingMode.Capped ? _interval : 1;

    public double FrameIntervalMilliseconds => _period * Interval;

    public void Configure(bool capped, double framesPerSecond)
    {
        lock (_configurationGate)
        {
            _requestedCap = capped && framesPerSecond > 0;
            _requestedTargetMilliseconds = framesPerSecond > 0 ? 1000 / framesPerSecond : 0;
            _configurationVersion++;
        }
    }

    public void Suspend()
    {
        Volatile.Write(ref _suspended, 1);
    }

    public bool MarkUpdate(long timestamp)
    {
        if (_updatePending &&
            (timestamp - _updateTimestamp) * _millisecondsPerTimestamp < MaximumStepMilliseconds) return false;
        _updatePending = true;
        _updateTimestamp = timestamp;
        return true;
    }

    public CanvasFrameTime BeginFrame(long timestamp)
    {
        ApplyConfiguration();
        _frameUpdated = _updatePending;
        if (!_updatePending) return new CanvasFrameTime(_totalTicks, 0, _frameCount);
        _updatePending = false;
        var start = _updateTimestamp;
        _frameStart = start;
        _frameCount++;
        var resumed = Interlocked.Exchange(ref _suspended, 0) == 1;
        double elapsed;
        if (_frameTimestamp == 0 || resumed)
        {
            elapsed = _frameTimestamp == 0 ? 0 : Math.Min(_period, MaximumStepMilliseconds);
            _realMilliseconds = _totalMilliseconds + elapsed;
        }
        else
        {
            var interval = (start - _frameTimestamp) * _millisecondsPerTimestamp;
            Observe(interval);
            var step = Math.Clamp(interval, 0, MaximumStepMilliseconds);
            _realMilliseconds += step;
            elapsed = Snap(step);
            elapsed = Math.Clamp(elapsed + (_realMilliseconds - _totalMilliseconds - elapsed) * DriftGain, 0,
                MaximumStepMilliseconds);
        }

        _frameTimestamp = start;
        _totalMilliseconds += elapsed;
        var total = (long)Math.Round(_totalMilliseconds * TimeSpan.TicksPerMillisecond);
        var elapsedTicks = total - _totalTicks;
        _totalTicks = total;
        return new CanvasFrameTime(total, elapsedTicks, _frameCount);
    }

    public CanvasLoopSchedule EndFrame(long timestamp)
    {
        if (_mode != CanvasPacingMode.Capped) return new CanvasLoopSchedule(false, 0, false);
        if (!_frameUpdated) return new CanvasLoopSchedule(_targetTicks > 0, _targetTicks, false);
        var drawn = (timestamp - _frameStart) * _millisecondsPerTimestamp;
        var target = Math.Max((_interval - 0.5 - LateAllowance) * _period - drawn, ShortestTarget * _period);
        _targetTicks = Math.Max(1, (long)Math.Round(target * TimeSpan.TicksPerMillisecond));
        return new CanvasLoopSchedule(true, _targetTicks, true);
    }

    private void ApplyConfiguration()
    {
        if (Volatile.Read(ref _configurationVersion) == _appliedConfigurationVersion) return;
        lock (_configurationGate)
        {
            _cap = _requestedCap;
            _targetMilliseconds = _requestedTargetMilliseconds;
            _appliedConfigurationVersion = _configurationVersion;
        }

        if (_mode != CanvasPacingMode.Calibrating) SelectMode();
    }

    private double Snap(double step)
    {
        if (_period <= 0) return step;
        var multiple = Multiple(step, _period);
        return multiple > 0 ? multiple * _period : step;
    }

    private static int Multiple(double interval, double period)
    {
        var multiple = Math.Round(interval / period);
        if (multiple < 1 || multiple > LongestMultiple) return 0;
        var tolerance = Math.Min(WideMatchTolerance * period,
            Math.Max(MatchTolerance * period, JitterToleranceMilliseconds));
        return Math.Abs(interval - multiple * period) <= tolerance ? (int)multiple : 0;
    }

    private void Observe(double interval)
    {
        if (interval <= 0) return;
        if (_mode == CanvasPacingMode.Calibrating)
        {
            _calibration[_calibrationCount++] = interval;
            if (_calibrationCount < CalibrationFrames) return;
            _calibrationCount = 0;
            Array.Copy(_calibration, _sorted, CalibrationFrames);
            Array.Sort(_sorted);
            var seed = _sorted[CalibrationFrames / 4];
            if (seed is < ShortestPeriodMilliseconds or > LongestPeriodMilliseconds) return;
            var sum = 0.0;
            var multiples = 0;
            foreach (var sample in _calibration)
            {
                var count = Multiple(sample, seed);
                if (count == 0) continue;
                sum += sample;
                multiples += count;
            }

            if (multiples < CalibrationFrames / 2) return;
            _period = sum / multiples;
            ResetHistory();
            SelectMode();
            return;
        }

        if (interval > (LongestMultiple + 0.5) * _period) return;
        var multiple = Multiple(interval, _period);
        if (multiple > 0) _period += (interval / multiple - _period) * PeriodGain;
        Record(multiple switch { 0 => Mismatch, 1 => Single, _ => Several });
        if (_historyCount < HistoryLength) return;
        if (_mismatches * 2 >= HistoryLength || _mode == CanvasPacingMode.Variable && _singles == 0)
        {
            _mode = CanvasPacingMode.Calibrating;
            _calibrationCount = 0;
            _targetTicks = 0;
            ResetHistory();
        }
    }

    private void Record(byte kind)
    {
        var previous = _history[_historyIndex];
        if (previous == Mismatch) _mismatches--;
        if (previous == Single) _singles--;
        _history[_historyIndex] = kind;
        if (kind == Mismatch) _mismatches++;
        if (kind == Single) _singles++;
        _historyIndex = (_historyIndex + 1) % HistoryLength;
        if (_historyCount < HistoryLength) _historyCount++;
    }

    private void ResetHistory()
    {
        Array.Clear(_history);
        _historyIndex = 0;
        _historyCount = 0;
        _mismatches = 0;
        _singles = 0;
    }

    private void SelectMode()
    {
        var interval = _cap ? Math.Max(1, (int)Math.Round(_targetMilliseconds / _period - FasterRateBias)) : 1;
        var mode = interval > 1 ? CanvasPacingMode.Capped : CanvasPacingMode.Variable;
        if (mode != _mode || interval != _interval) _targetTicks = 0;
        _interval = interval;
        _mode = mode;
    }
}
