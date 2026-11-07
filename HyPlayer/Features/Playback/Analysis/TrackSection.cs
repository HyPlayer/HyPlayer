using System.Text.Json.Serialization;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class TrackSection
{
    public const double LowLevelDb = -6;

    public double Start { get; set; }
    public double End { get; set; }
    public double Level { get; set; }
    public double Strength { get; set; }
    public int Repeat { get; set; } = -1;

    [JsonIgnore]
    public double Length => End - Start;

    [JsonIgnore]
    public bool IsLow => Level <= LowLevelDb;
}
