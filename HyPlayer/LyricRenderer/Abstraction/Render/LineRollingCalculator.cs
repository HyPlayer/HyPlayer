namespace HyPlayer.LyricRenderer.Abstraction.Render;

public abstract class LineRollingCalculator
{
    public abstract double CalculateCurrentY(double fromY, double targetY, int gap, long startTime, long currentTime);
}