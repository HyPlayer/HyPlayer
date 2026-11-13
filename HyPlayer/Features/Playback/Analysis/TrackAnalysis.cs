using System;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class TrackAnalysis
{
    public const int CurrentVersion = 5;
    public const int ModelBeatVersion = 3;
    public const int DspBeatVersion = 4;
    public const string DspAnalyzer = "dsp";

    public int Version { get; set; } = CurrentVersion;
    public string TrackKey { get; set; } = string.Empty;
    public string BeatAnalyzer { get; set; } = "dsp";
    public string KeyAnalyzer { get; set; } = "dsp";
    public double Duration { get; set; }
    public double SoundStart { get; set; }
    public double SoundEnd { get; set; }
    public double IntegratedLoudness { get; set; } = -70;
    public double Bpm { get; set; }
    public double BeatConfidence { get; set; }
    public double[] Beats { get; set; } = Array.Empty<double>();
    public double[] Downbeats { get; set; } = Array.Empty<double>();
    public double DownbeatConfidence { get; set; } = 1;
    public double IntroEnd { get; set; } = -1;
    public double OutroStart { get; set; } = -1;
    public double[] BarEnergies { get; set; } = Array.Empty<double>();
    public int Key { get; set; } = -1;
    public double KeyConfidence { get; set; }
    public double[] Envelope { get; set; } = Array.Empty<double>();
    public double EnvelopeRate { get; set; } = 10;
    public string VocalAnalyzer { get; set; } = string.Empty;
    public double[]? VocalIntroWindow { get; set; }
    public double[]? VocalIntroCurve { get; set; }
    public double[]? VocalOutroWindow { get; set; }
    public double[]? VocalOutroCurve { get; set; }
    public TempoRun[]? TempoRuns { get; set; }
    public TrackSection[]? Sections { get; set; }

    public bool HasBeatGrid => Beats.Length >= 16 && Bpm > 0;
    public bool IsCurrentVersion => Version == CurrentVersion;
    public bool HasCurrentBeats => Version >= DspBeatVersion || Version == ModelBeatVersion && BeatAnalyzer != DspAnalyzer;
}
