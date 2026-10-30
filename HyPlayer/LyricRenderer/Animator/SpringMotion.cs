using System;

namespace HyPlayer.LyricRenderer.Animator;

internal struct SpringMotion
{
    private const double RestDistance = 0.02;
    private const double RestSpeed = 0.5;

    public double Target;
    public double StartOffset;
    public double StartVelocity;
    public long StartTick;
    public double AngularFrequency;
    public double DampingRatio;
    public bool Settled;

    public static double FrequencyFor(double responseSeconds) =>
        2 * Math.PI / Math.Max(0.01, responseSeconds);

    public static double SettleSeconds(double responseSeconds) => 0.94 * responseSeconds;

    public readonly void Evaluate(long tick, out double position, out double velocity)
    {
        if (Settled)
        {
            position = Target;
            velocity = 0;
            return;
        }

        var time = Math.Max(0, (tick - StartTick) / (double)TimeSpan.TicksPerSecond);
        var frequency = AngularFrequency;
        var damping = DampingRatio;
        if (damping >= 0.999999)
        {
            var slope = StartVelocity + frequency * StartOffset;
            var envelope = Math.Exp(-frequency * time);
            position = Target + (StartOffset + slope * time) * envelope;
            velocity = (StartVelocity - frequency * slope * time) * envelope;
            return;
        }

        var decay = damping * frequency;
        var damped = frequency * Math.Sqrt(1 - damping * damping);
        var quadrature = (StartVelocity + decay * StartOffset) / damped;
        var decayed = Math.Exp(-decay * time);
        var (sine, cosine) = Math.SinCos(damped * time);
        position = Target + decayed * (StartOffset * cosine + quadrature * sine);
        velocity = decayed * ((quadrature * damped - decay * StartOffset) * cosine -
                              (StartOffset * damped + decay * quadrature) * sine);
    }

    public void Retarget(long tick, double target, double responseSeconds, double dampingRatio)
    {
        Evaluate(tick, out var position, out var velocity);
        Target = target;
        StartOffset = position - target;
        StartVelocity = velocity;
        StartTick = tick;
        AngularFrequency = FrequencyFor(responseSeconds);
        DampingRatio = Math.Clamp(dampingRatio, 0.05, 1);
        Settled = false;
    }

    public void Snap(double target)
    {
        Target = target;
        StartOffset = 0;
        StartVelocity = 0;
        Settled = true;
    }

    public void SettleIfAtRest(double position, double velocity)
    {
        if (Settled || Math.Abs(position - Target) > RestDistance || Math.Abs(velocity) > RestSpeed) return;
        StartOffset = 0;
        StartVelocity = 0;
        Settled = true;
    }
}

internal sealed class LineSpringField
{
    private Line[] _lines = [];

    public int Count { get; private set; }

    public void Reset(int count)
    {
        if (_lines.Length < count) _lines = new Line[count];
        Count = count;
        for (var index = 0; index < count; index++)
        {
            _lines[index] = default;
            _lines[index].Spring.Settled = true;
        }
    }

    public double Goal(int index) => _lines[index].Goal;

    public bool IsMoving(int index) => !_lines[index].Spring.Settled || _lines[index].HasPending;

    public void Snap(int index, double target)
    {
        ref var line = ref _lines[index];
        line.Spring.Snap(target);
        line.Goal = target;
        line.HasPending = false;
    }

    public void Shift(int index, double delta)
    {
        ref var line = ref _lines[index];
        line.Spring.Target += delta;
        line.Goal += delta;
    }

    public void Retarget(int index, double goal, long tick, long delayTicks, double responseSeconds,
        double dampingRatio)
    {
        ref var line = ref _lines[index];
        line.Goal = goal;
        if (delayTicks <= 0)
        {
            line.HasPending = false;
            line.Spring.Retarget(tick, goal, responseSeconds, dampingRatio);
            return;
        }

        line.HasPending = true;
        line.PendingTick = tick + delayTicks;
        line.PendingResponse = responseSeconds;
        line.PendingDamping = dampingRatio;
    }

    public double Advance(int index, long tick, out double velocity)
    {
        ref var line = ref _lines[index];
        if (line.HasPending && tick >= line.PendingTick)
        {
            line.HasPending = false;
            line.Spring.Retarget(line.PendingTick, line.Goal, line.PendingResponse, line.PendingDamping);
        }

        line.Spring.Evaluate(tick, out var position, out velocity);
        if (!line.HasPending) line.Spring.SettleIfAtRest(position, velocity);
        return position;
    }

    private struct Line
    {
        public SpringMotion Spring;
        public double Goal;
        public long PendingTick;
        public double PendingResponse;
        public double PendingDamping;
        public bool HasPending;
    }
}
