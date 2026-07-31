using System;
using HyPlayer.PlayCore.Abstraction.Models.Resources;

namespace HyPlayer.Platform.Playback.AudioServices;

internal static class AudioGainCalculator
{
    internal const double ReferenceLoudnessDb = -18d;
    internal const double TargetLoudnessDb = -18d;
    internal const double PeakHeadroomDb = -1d;
    internal const double MinimumGainDb = -24d;
    internal const double MaximumGainDb = 12d;
    private const double MaximumReasonablePeak = 4d;

    internal static double Calculate(IAudioGainMetadata? metadata, bool enabled)
    {
        if (!enabled || metadata is not { SupportsAudioGain: true, GainDb: { } providerGainDb })
            return 1d;

        return Calculate(providerGainDb, metadata.Peak);
    }

    internal static double Calculate(double providerGainDb, double? peak)
    {
        if (!double.IsFinite(providerGainDb))
            return 1d;

        var gainDb = providerGainDb + (TargetLoudnessDb - ReferenceLoudnessDb);
        gainDb = Math.Clamp(gainDb, MinimumGainDb, MaximumGainDb);

        if (peak is { } linearPeak
            && double.IsFinite(linearPeak)
            && linearPeak > 0d
            && linearPeak <= MaximumReasonablePeak)
        {
            var peakDb = 20d * Math.Log10(linearPeak);
            gainDb = Math.Min(gainDb, PeakHeadroomDb - peakDb);
        }
        else if (gainDb > 0d)
        {
            // Positive gain without trustworthy peak data can clip the decoded signal.
            return 1d;
        }

        return Math.Pow(10d, gainDb / 20d);
    }
}
