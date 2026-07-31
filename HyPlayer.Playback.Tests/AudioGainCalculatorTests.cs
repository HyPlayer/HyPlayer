using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class AudioGainCalculatorTests
{
    [Test]
    public void Documented_gain_is_converted_from_decibels_to_linear_volume()
    {
        var actual = AudioGainCalculator.Calculate(-9.006d, 1d);

        EnsureClose(actual, Math.Pow(10d, -9.006d / 20d));
    }

    [Test]
    public void Positive_gain_is_limited_by_peak_headroom()
    {
        var actual = AudioGainCalculator.Calculate(6d, 0.5d);

        EnsureClose(actual * 0.5d, Math.Pow(10d, AudioGainCalculator.PeakHeadroomDb / 20d));
    }

    [Test]
    public void Positive_gain_without_peak_is_not_applied()
    {
        var actual = AudioGainCalculator.Calculate(6d, null);

        EnsureClose(actual, 1d);
    }

    [Test]
    public void Invalid_gain_is_not_applied()
    {
        var actual = AudioGainCalculator.Calculate(double.NaN, 1d);

        EnsureClose(actual, 1d);
    }

    [Test]
    public void Disabled_or_unsupported_metadata_is_not_applied()
    {
        var supported = new FakeAudioGainMetadata(true, -9d, 1d);
        var unsupported = new FakeAudioGainMetadata(false, -9d, 1d);

        EnsureClose(AudioGainCalculator.Calculate(supported, false), 1d);
        EnsureClose(AudioGainCalculator.Calculate(unsupported, true), 1d);
    }

    private static void EnsureClose(double actual, double expected)
    {
        if (Math.Abs(actual - expected) > 0.000001d)
            throw new InvalidOperationException($"Expected {expected}, but got {actual}.");
    }

    private sealed record FakeAudioGainMetadata(
        bool SupportsAudioGain,
        double? GainDb,
        double? Peak) : IAudioGainMetadata;
}
