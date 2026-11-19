using System;

namespace HyPlayer.Features.Playback.AutoMix;

public sealed class NodeCrossfade
{
    private readonly int _quantum;
    private readonly int _steps;
    private readonly double _rampFrames;
    private readonly bool _nodeFadesIn;

    public NodeCrossfade(long start, int quantum, int steps, int sampleRate, bool nodeFadesIn)
    {
        Start = start;
        _quantum = Math.Max(1, quantum);
        _steps = Math.Max(1, steps);
        _rampFrames = Math.Max(1, sampleRate / 1000.0);
        _nodeFadesIn = nodeFadesIn;
    }

    public long Start { get; }
    public long End => Start + (long)_steps * _quantum;

    public static long AlignUp(long position, long gridPoint, int quantum)
    {
        var step = Math.Max(1, quantum);
        var offset = (gridPoint - position) % step;
        if (offset < 0) offset += step;
        return position + offset;
    }

    public double NodeStep(long step)
    {
        var t = step < 0 ? 0 : step >= _steps - 1 ? 1 : (step + 1) / (double)_steps;
        var rise = 0.5 - 0.5 * Math.Cos(Math.PI * t);
        return _nodeFadesIn ? rise : 1 - rise;
    }

    public double NodeTarget(long played) => NodeStep(played < Start ? -1 : (played - Start) / _quantum);

    public double NodeWeightAt(long sample)
    {
        if (sample < Start) return NodeStep(-1);
        var distance = sample - Start;
        var step = distance / _quantum;
        var previous = NodeStep(step - 1);
        var current = NodeStep(step);
        var ramp = Math.Min(1, (distance - step * _quantum) / _rampFrames);
        return previous + (current - previous) * ramp;
    }

    public double VoiceWeightAt(long sample) => 1 - NodeWeightAt(sample);
}
