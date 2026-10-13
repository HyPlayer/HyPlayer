using System;

namespace HyPlayer.LyricRenderer.Animator;

internal struct LyricScalarTransition
{
    internal const float SettleRate = 7.4f;

    private bool _initialized;
    private float _target;
    private float _offset;
    private float _velocity;
    private long _startTime;
    private float _rate;
    private float _duration;

    public float Update(float target, long time, float duration, bool seek)
    {
        if (!_initialized || seek || time < _startTime)
        {
            _initialized = true;
            _target = target;
            _offset = _velocity = _rate = 0;
            _startTime = time;
            return target;
        }

        if (Math.Abs(target - _target) > 0.0001f)
        {
            Evaluate(time, out var value, out var velocity);
            _target = target;
            _startTime = time;
            if (duration <= 0)
            {
                _offset = _velocity = _rate = 0;
                return target;
            }

            _offset = value - target;
            _velocity = velocity;
            _rate = SettleRate / duration;
            _duration = duration;
            return value;
        }

        Evaluate(time, out var current, out _);
        return current;
    }

    private readonly void Evaluate(long time, out float value, out float velocity)
    {
        float elapsed = time - _startTime;
        if (_rate <= 0 || elapsed >= 2 * _duration)
        {
            value = _target;
            velocity = 0;
            return;
        }

        var slope = _velocity + _rate * _offset;
        var envelope = MathF.Exp(-_rate * elapsed);
        value = _target + (_offset + slope * elapsed) * envelope;
        velocity = (_velocity - _rate * slope * elapsed) * envelope;
    }
}
