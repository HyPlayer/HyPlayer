using System;

namespace HyPlayer.LyricRenderer.Animator;

internal struct RenderScalarTransition
{
    private bool _initialized;
    private float _start;
    private float _target;
    private long _startTick;
    private float _durationTicks;

    public void Reset(float value, long tick)
    {
        _initialized = true;
        _start = _target = value;
        _startTick = tick;
        _durationTicks = 0;
    }

    public float Update(float target, long tick, float durationMilliseconds, bool snap)
    {
        if (!_initialized || snap || tick < _startTick)
        {
            Reset(target, tick);
            return target;
        }

        if (Math.Abs(target - _target) > 0.0001f)
        {
            _start = Value(tick);
            _target = target;
            _startTick = tick;
            _durationTicks = durationMilliseconds * TimeSpan.TicksPerMillisecond;
        }

        return Value(tick);
    }

    private readonly float Value(long tick)
    {
        var progress = _durationTicks <= 0 ? 1 : Math.Clamp((tick - _startTick) / _durationTicks, 0, 1);
        return _start + (_target - _start) * progress * progress * (3 - 2 * progress);
    }
}
