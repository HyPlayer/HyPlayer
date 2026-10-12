using System;

namespace HyPlayer.LyricRenderer.Animator;

/// <summary>A fixed numeric Circle/Out transition with no parameter dictionaries.</summary>
internal struct LyricScalarTransition
{
    private bool _initialized;
    private float _start;
    private float _target;
    private long _startTime;
    private float _duration;

    public float Update(float target, long time, float duration, bool seek)
    {
        if (!_initialized || seek || time < _startTime)
        {
            _initialized = true;
            _start = _target = target;
            _startTime = time;
            _duration = duration;
            return target;
        }
        if (Math.Abs(target - _target) > 0.0001f)
        {
            _start = Value(time);
            _target = target;
            _startTime = time;
            _duration = duration;
        }
        return Value(time);
    }

    private readonly float Value(long time)
    {
        var t = _duration <= 0 ? 1 : Math.Clamp((time - _startTime) / _duration, 0, 1);
        var eased = MathF.Sqrt(Math.Max(0, 1 - (t - 1) * (t - 1)));
        return _start + (_target - _start) * eased;
    }
}
