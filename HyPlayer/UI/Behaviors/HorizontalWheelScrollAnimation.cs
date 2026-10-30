#nullable enable

using System;

namespace HyPlayer.UI.Behaviors;

internal enum WheelScrollOutcome
{
    PassThrough,
    Consumed
}

internal sealed class HorizontalWheelScrollAnimation
{
    public const int NotchDelta = 120;
    public const double DurationSeconds = 0.25;
    public const double EdgeTolerance = 1;

    private double _elapsed;
    private double _from;

    public bool IsRunning { get; private set; }

    public double Position { get; private set; }

    public double Target { get; private set; }

    public static bool IsNotch(int wheelDelta) => wheelDelta != 0 && wheelDelta % NotchDelta == 0;

    public static int DirectionOf(int wheelDelta, bool isHorizontalWheel) =>
        isHorizontalWheel ? Math.Sign(wheelDelta) : -Math.Sign(wheelDelta);

    public static double StepFor(double viewport, int wheelDelta)
    {
        if (viewport <= 0 || wheelDelta == 0) return 0;
        var perNotch = Math.Max(48, Math.Round(viewport * 0.15));
        return Math.Min(Math.Floor(viewport), Math.Round(Math.Abs(wheelDelta) * perNotch / NotchDelta));
    }

    public static bool IsAtEdge(double offset, double maxOffset, int direction) =>
        direction < 0 ? offset <= EdgeTolerance : offset >= maxOffset - EdgeTolerance;

    public WheelScrollOutcome Push(double offset, double maxOffset, double delta)
    {
        var direction = Math.Sign(delta);
        if (!IsRunning)
        {
            if (direction == 0 || maxOffset <= EdgeTolerance || IsAtEdge(offset, maxOffset, direction))
                return WheelScrollOutcome.PassThrough;
            Position = offset;
            Target = offset;
        }

        var next = Math.Clamp(Target + delta, 0, Math.Max(0, maxOffset));
        if (IsRunning && next == Target) return WheelScrollOutcome.Consumed;
        Target = next;
        _from = Position;
        _elapsed = 0;
        IsRunning = Target != Position;
        return WheelScrollOutcome.Consumed;
    }

    public double Advance(double seconds)
    {
        if (!IsRunning) return Position;
        _elapsed += Math.Max(0, seconds);
        var progress = Math.Min(1, _elapsed / DurationSeconds);
        if (progress >= 1)
        {
            Position = Target;
            IsRunning = false;
            return Position;
        }

        var remaining = 1 - progress;
        Position = _from + (Target - _from) * (1 - remaining * remaining * remaining);
        return Position;
    }

    public double Complete()
    {
        Position = Target;
        IsRunning = false;
        return Position;
    }

    public void Stop()
    {
        IsRunning = false;
    }
}
