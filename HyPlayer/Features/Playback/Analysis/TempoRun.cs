using System;
using System.Text.Json.Serialization;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class TempoRun
{
    public double Start { get; set; }
    public double End { get; set; }
    public double Period { get; set; }
    public double Phase { get; set; }
    public int Beats { get; set; }
    public double Deviation { get; set; }

    [JsonIgnore]
    public double Bpm => Period > 0 ? 60 / Period : 0;

    public bool Covers(double from, double to)
    {
        var slack = 0.5 * Period;
        return Period > 0 && from >= Start - slack && to <= End + slack;
    }

    public double Snap(double time)
    {
        return Period > 0 ? Phase + Math.Round((time - Phase) / Period) * Period : time;
    }
}
