using System;
using System.Diagnostics;
using System.Numerics;
using HyPlayer.UWP.Chopin.Utils;

namespace HyPlayer.UI.Effects.LikeApple;

/// <summary>
/// Converts the player's raw FFT snapshots into the low-frequency pulse used
/// by the Apple Music-inspired backdrop.
/// </summary>
internal sealed class LikeAppleSpectrumAnalysis
{
    private const double MissingReportSeconds = 0.25;
    private const float DefaultReportSeconds = 1f / 60f;
    private const float AttackSeconds = 0.055f;
    private const float ReleaseSeconds = 0.24f;
    private const float FeatureBaselineAttackSeconds = 1.1f;
    private const float FeatureBaselineReleaseSeconds = 0.16f;
    private const int ConfirmationReportCount = 3;
    private const int PeakHoldReportCount = 5;
    private const float SilenceFloorDecibels = -72f;
    private const float BassLevelFloorDecibels = -50f;
    private const float BassLevelCeilingDecibels = -18f;
    private const float BassDominanceFloorDecibels = 0f;
    private const float BassDominanceCeilingDecibels = 8f;
    private const float BassRiseFloorDecibels = 1.2f;
    private const float BassRiseCeilingDecibels = 7f;
    private const float SharpAttackLevelFloorDecibels = -45f;
    private const float SharpAttackRiseFloorDecibels = 7f;
    private const float SharpAttackRiseCeilingDecibels = 14f;
    private const float SharpAttackReleaseSeconds = 0.09f;
    private const float HarmonicBassConfidenceFloor = 0.12f;
    private const float HarmonicBassConfidenceCeiling = 0.3f;
    private const float HarmonicBassAttackBoost = 0.9f;
    private const float SustainedBassResponse = 0.1f;

    private const float ImagePulsePowerMix = 0.1f;
    private const float ImagePulseIntensity = 0.33f;
    private static readonly FrequencyBand LowBassBand = new(30f, 105f);
    private static readonly FrequencyBand BassNoteBand = new(75f, 155f);
    private static readonly FrequencyBand UpperBassBand = new(145f, 210f);
    private static readonly FrequencyBand LowMidReferenceBand = new(155f, 380f);
    private static readonly FrequencyBand MidReferenceBand = new(380f, 760f);

    private readonly FFTProcessor _fftProcessor;
    private readonly float[] _fftMagnitudes = new float[FFTProcessor.SpectrumBinCount];
    private readonly float[] _unprocessedReportHistory = new float[ConfirmationReportCount];
    private readonly float[] _peakReportHistory = new float[PeakHoldReportCount];

    private long _lastFftVersion;
    private long _lastReportTimestamp;
    private long _lastAnalysisTimestamp;
    private long _lastSmoothingTimestamp;
    private int _unprocessedReportWriteIndex;
    private int _availableUnprocessedReportCount;
    private int _peakReportWriteIndex;
    private float _reportedPower;
    private float _slowBassDecibels = SilenceFloorDecibels;
    private float _slowReferenceDecibels = SilenceFloorDecibels;
    private float _previousBassDecibels = SilenceFloorDecibels;
    private float _sharpAttack;
    private bool _featureStateInitialized;
    private Vector4 _power;

    public LikeAppleSpectrumAnalysis(FFTProcessor fftProcessor)
    {
        _fftProcessor = fftProcessor;
    }

    public Vector4 GetImageScales(bool isPlaying, float pulseScale = 1f)
    {
        long now = Stopwatch.GetTimestamp();
        if (isPlaying &&
            _fftProcessor.TryCopyFftMagnitudes(
                _fftMagnitudes,
                out int sampleRate,
                out long version) &&
            version != _lastFftVersion)
        {
            float reportSeconds = _lastAnalysisTimestamp == 0
                ? DefaultReportSeconds
                : (float)Math.Clamp(
                    (now - _lastAnalysisTimestamp) / (double)Stopwatch.Frequency,
                    0d,
                    0.25d);
            _lastAnalysisTimestamp = now;
            _lastFftVersion = version;
            _reportedPower = GetConfirmedPeakResponse(
                GetBassResponse(sampleRate, reportSeconds));
            _lastReportTimestamp = now;
        }

        bool reportIsCurrent = isPlaying &&
            _lastReportTimestamp != 0 &&
            (now - _lastReportTimestamp) / (double)Stopwatch.Frequency <=
                MissingReportSeconds;
        Vector4 target = reportIsCurrent
            ? new Vector4(_reportedPower, _reportedPower, _reportedPower, 0f)
            : Vector4.Zero;

        double elapsedSeconds = _lastSmoothingTimestamp == 0
            ? DefaultReportSeconds
            : (now - _lastSmoothingTimestamp) / (double)Stopwatch.Frequency;
        _lastSmoothingTimestamp = now;
        _power = SmoothPower(
            _power,
            target,
            (float)Math.Clamp(elapsedSeconds, 0d, 0.25d));
        if (_power.LengthSquared() < 0.00000001f) _power = Vector4.Zero;

        float effectivePulseScale = float.IsFinite(pulseScale)
            ? Math.Clamp(pulseScale, 0f, 10f)
            : 1f;
        float processedPowerX = ProcessSpectrumPower(_power.X);
        float processedPowerY = ProcessSpectrumPower(_power.Y);
        float blendedPower = Lerp(
            processedPowerX,
            processedPowerY,
            ImagePulsePowerMix);
        float imageScale = 1f +
            ImagePulseIntensity * blendedPower * blendedPower *
            effectivePulseScale;
        return new Vector4(imageScale, imageScale, imageScale, 1f);
    }

    public void Stop()
    {
        Array.Clear(_fftMagnitudes);
        Array.Clear(_unprocessedReportHistory);
        Array.Clear(_peakReportHistory);
        _lastFftVersion = 0;
        _lastReportTimestamp = 0;
        _lastAnalysisTimestamp = 0;
        _lastSmoothingTimestamp = 0;
        _unprocessedReportWriteIndex = 0;
        _availableUnprocessedReportCount = 0;
        _peakReportWriteIndex = 0;
        _reportedPower = 0f;
        _slowBassDecibels = SilenceFloorDecibels;
        _slowReferenceDecibels = SilenceFloorDecibels;
        _previousBassDecibels = SilenceFloorDecibels;
        _sharpAttack = 0f;
        _featureStateInitialized = false;
        _power = Vector4.Zero;
    }

    private float GetBassResponse(int sampleRate, float reportSeconds)
    {
        double lowBassPower = GetAverageBandPower(LowBassBand, sampleRate);
        double bassNotePower = GetAverageBandPower(BassNoteBand, sampleRate);
        double coreBassPower = Math.Max(lowBassPower, bassNotePower * 0.9d);
        double upperBassPower = GetAverageBandPower(UpperBassBand, sampleRate);
        double supportedUpperBassPower = Math.Min(
            upperBassPower,
            coreBassPower * 1.35d);
        double bassPower = coreBassPower + supportedUpperBassPower * 0.2d;

        double lowMidPower = GetAverageBandPower(LowMidReferenceBand, sampleRate);
        double midPower = GetAverageBandPower(MidReferenceBand, sampleRate);
        double referencePower = Math.Max(
            lowMidPower * 2.3d,
            midPower * 1.6d);

        float bassDecibels = PowerToDecibels(bassPower);
        float referenceDecibels = PowerToDecibels(referencePower);
        if (!_featureStateInitialized)
        {
            _slowBassDecibels = MathF.Max(
                SilenceFloorDecibels,
                bassDecibels - BassRiseCeilingDecibels);
            _slowReferenceDecibels = referenceDecibels;
            _previousBassDecibels = bassDecibels;
            _featureStateInitialized = true;
        }

        float frameBassRise = MathF.Max(
            0f,
            bassDecibels - _previousBassDecibels);
        _previousBassDecibels = bassDecibels;

        float bassRise = MathF.Max(0f, bassDecibels - _slowBassDecibels);
        float referenceRise = MathF.Max(
            0f,
            referenceDecibels - _slowReferenceDecibels);
        float dominance = SmoothRange(
            bassDecibels - referenceDecibels,
            BassDominanceFloorDecibels,
            BassDominanceCeilingDecibels);
        float sharpAttackTarget = bassDecibels >= SharpAttackLevelFloorDecibels
            ? SmoothRange(
                frameBassRise,
                SharpAttackRiseFloorDecibels,
                SharpAttackRiseCeilingDecibels)
            : 0f;
        float sharpAttackDecay = MathF.Exp(
            -reportSeconds / SharpAttackReleaseSeconds);
        _sharpAttack = MathF.Max(
            sharpAttackTarget,
            _sharpAttack * sharpAttackDecay);

        float harmonicBassConfidence = SmoothRange(
            dominance,
            HarmonicBassConfidenceFloor,
            HarmonicBassConfidenceCeiling) *
            _sharpAttack * HarmonicBassAttackBoost;
        float bassConfidence = MathF.Max(dominance, harmonicBassConfidence);
        float referenceRiseRejection = 0.7f - dominance * 0.35f;
        float bassOnlyRise = bassRise -
            referenceRise * referenceRiseRejection;

        _slowBassDecibels = SmoothFeatureBaseline(
            _slowBassDecibels,
            bassDecibels,
            reportSeconds);
        _slowReferenceDecibels = SmoothFeatureBaseline(
            _slowReferenceDecibels,
            referenceDecibels,
            reportSeconds);

        float level = SmoothRange(
            bassDecibels,
            BassLevelFloorDecibels,
            BassLevelCeilingDecibels);
        float transient = SmoothRange(
            bassOnlyRise,
            BassRiseFloorDecibels,
            BassRiseCeilingDecibels);
        float response = level * bassConfidence *
            (SustainedBassResponse +
                (1f - SustainedBassResponse) * transient);
        return Math.Clamp(response, 0f, 1f);
    }

    private float GetConfirmedPeakResponse(float unprocessedResponse)
    {
        _unprocessedReportHistory[_unprocessedReportWriteIndex] =
            unprocessedResponse;
        _unprocessedReportWriteIndex =
            (_unprocessedReportWriteIndex + 1) %
            _unprocessedReportHistory.Length;
        _availableUnprocessedReportCount = Math.Min(
            _availableUnprocessedReportCount + 1,
            _unprocessedReportHistory.Length);

        float confirmedResponse = 0f;
        if (_availableUnprocessedReportCount == 2)
        {
            confirmedResponse = MathF.Min(
                _unprocessedReportHistory[0],
                _unprocessedReportHistory[1]);
        }
        else if (_availableUnprocessedReportCount >= 3)
        {
            confirmedResponse = MedianOfThree(
                _unprocessedReportHistory[0],
                _unprocessedReportHistory[1],
                _unprocessedReportHistory[2]);
        }

        _peakReportHistory[_peakReportWriteIndex] = confirmedResponse;
        _peakReportWriteIndex =
            (_peakReportWriteIndex + 1) % _peakReportHistory.Length;

        float peakResponse = 0f;
        for (int index = 0; index < _peakReportHistory.Length; index++)
        {
            peakResponse = MathF.Max(
                peakResponse,
                _peakReportHistory[index]);
        }
        return peakResponse;
    }

    private double GetAverageBandPower(FrequencyBand band, int sampleRate)
    {
        int firstBin = Math.Max(1, (int)MathF.Ceiling(
            band.Minimum * FFTProcessor.FftSize / sampleRate));
        int lastBin = Math.Min(
            FFTProcessor.SpectrumBinCount - 1,
            (int)MathF.Floor(
                band.Maximum * FFTProcessor.FftSize / sampleRate));
        if (lastBin < firstBin) return 0d;

        double squaredMagnitude = 0d;
        for (int bin = firstBin; bin <= lastBin; bin++)
        {
            float magnitude = _fftMagnitudes[bin] * 4f;
            if (float.IsFinite(magnitude))
                squaredMagnitude += magnitude * magnitude;
        }

        int binCount = lastBin - firstBin + 1;
        return squaredMagnitude / (3d * binCount);
    }

    private static float PowerToDecibels(double averageBandPower)
    {
        return (float)(10d * Math.Log10(
            Math.Max(averageBandPower, 1e-12d)));
    }

    private static float MedianOfThree(float first, float second, float third)
    {
        return first + second + third -
            MathF.Min(first, MathF.Min(second, third)) -
            MathF.Max(first, MathF.Max(second, third));
    }

    private static float SmoothFeatureBaseline(
        float current,
        float target,
        float seconds)
    {
        float timeConstant = target > current
            ? FeatureBaselineAttackSeconds
            : FeatureBaselineReleaseSeconds;
        float mix = 1f - MathF.Exp(-seconds / timeConstant);
        return MathF.Max(
            SilenceFloorDecibels,
            current + (target - current) * mix);
    }

    private static Vector4 SmoothPower(Vector4 current, Vector4 target, float seconds)
    {
        return new Vector4(
            SmoothPower(current.X, target.X, seconds),
            SmoothPower(current.Y, target.Y, seconds),
            SmoothPower(current.Z, target.Z, seconds),
            SmoothPower(current.W, target.W, seconds));
    }

    private static float SmoothPower(float current, float target, float seconds)
    {
        float timeConstant = target > current ? AttackSeconds : ReleaseSeconds;
        float mix = 1f - MathF.Exp(-seconds / timeConstant);
        return current + (target - current) * mix;
    }

    private static float ProcessSpectrumPower(float power)
    {
        float x = Math.Clamp(power, 0f, 1f);
        return x * x * x * (x * (x * 6f - 15f) + 10f);
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + (to - from) * amount;
    }

    private static float SmoothRange(float value, float floor, float ceiling)
    {
        float normalized = Math.Clamp(
            (value - floor) / (ceiling - floor),
            0f,
            1f);
        return normalized * normalized * (3f - 2f * normalized);
    }

    private readonly record struct FrequencyBand(float Minimum, float Maximum);
}
