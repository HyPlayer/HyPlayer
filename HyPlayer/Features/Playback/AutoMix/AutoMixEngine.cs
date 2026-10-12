using System;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public interface IAutoMixHost
{
    int SamplesPerQuantum { get; }
    void SetVolume(AudioGraphPlaybackSource source, double volume);
    void SetSpeed(AudioGraphPlaybackSource source, double speed);
    void Play(AudioGraphPlaybackSource source);
    void Seek(AudioGraphPlaybackSource source, TimeSpan position);
}

public sealed class GraphMixHost : IAutoMixHost
{
    private readonly AudioGraphPlayer _graph;

    public GraphMixHost(AudioGraphPlayer graph) => _graph = graph;

    public int SamplesPerQuantum => _graph.SamplesPerQuantum;
    public void SetVolume(AudioGraphPlaybackSource source, double volume) => _graph.SetPlaybackSourceOutputVolume(volume, source);
    public void SetSpeed(AudioGraphPlaybackSource source, double speed) => _graph.SetPlaybackSourceSpeed(speed, source);
    public void Play(AudioGraphPlaybackSource source) => _graph.PlayPlaybackSource(source);
    public void Seek(AudioGraphPlaybackSource source, TimeSpan position) => _graph.SeekPlaybackSource(position, source);
}

public enum AutoMixStage
{
    Waiting,
    Handover,
    Overlap,
    RampBack,
    Aligning,
    Done,
    Aborted
}

public sealed class AutoMixEngine : IMixRenderSource, IDisposable
{
    private readonly IAutoMixHost _player;
    private readonly MixPlan _plan;
    private readonly MixRegion _regionA;
    private readonly MixRegion _regionB;
    private readonly AudioGraphPlaybackSource _sourceA;
    private readonly AudioGraphPlaybackSource _sourceB;
    private readonly int _sampleRate;
    private readonly int _clickRamp;
    private readonly double _volumeA;
    private readonly double _volumeB;
    private readonly double _nodeVolumeA;
    private readonly double _nodeVolumeB;
    private readonly long _exitIndexA;
    private readonly long _overlapFrames;
    private readonly long _handoverIndexA;

    private MixVoice? _voiceA;
    private MixVoice? _voiceB;
    private bool _pendingHandover;
    private bool _pendingHandBack;
    private bool _pendingAbort;
    private long _overlapEndRendered = -1;
    private long _rendered;
    private double _nodeSpeedB = 1;
    private double _predictedNodeIndexB = double.NaN;
    private int _alignStableQuanta;
    private bool _alignChased;
    private bool _alignRefined;
    private long _startRenderedB = -1;
    private double _limiterGain = 1;
    private volatile bool _forceHandBack;
    private double _forcedFadeSeconds;
    private int _fadeOutFrames;
    private long _fadeOutDone;
    private double _fadeOutBase;

    public AutoMixEngine(IAutoMixHost player, MixPlan plan, MixRegion regionA, MixRegion regionB,
        AudioGraphPlaybackSource sourceA, AudioGraphPlaybackSource sourceB, double volumeA, double volumeB,
        double nodeVolumeA, double nodeVolumeB)
    {
        _player = player;
        _plan = plan;
        _regionA = regionA;
        _regionB = regionB;
        _sourceA = sourceA;
        _sourceB = sourceB;
        _sampleRate = regionA.SampleRate;
        _clickRamp = Math.Max(1, (int)Math.Round(_sampleRate * 0.001));
        _volumeA = volumeA;
        _volumeB = volumeB;
        _nodeVolumeA = nodeVolumeA;
        _nodeVolumeB = nodeVolumeB;
        _exitIndexA = regionA.IndexOf(plan.AExit);
        _overlapFrames = Math.Max(1, (long)Math.Round(plan.Overlap * _sampleRate));
        _handoverIndexA = Math.Max(0, _exitIndexA - _sampleRate * 2);
    }

    public PlaybackTap? TapA { get; set; }
    public PlaybackTap? TapB { get; set; }
    public double MapA { get; set; } = double.NaN;
    public double MapB { get; set; } = double.NaN;

    public volatile AutoMixStage Stage = AutoMixStage.Waiting;
    public volatile bool MidpointReached;
    public volatile bool OutgoingFinished;
    public volatile bool BStarted;
    public volatile bool NeedsMapB;
    public volatile bool NeedsForcedHandBack;
    public double BContentSeconds => _voiceB?.ContentSeconds ?? double.NaN;
    public double SecondsSinceBStarted => _startRenderedB < 0 ? 0 : (_rendered - _startRenderedB) / (double)_sampleRate;
    public double NodeVolumeA => _nodeVolumeA;
    public double NodeVolumeB => _nodeVolumeB;
    public double Progress { get; private set; }
    public long HandoverIndexA => _handoverIndexA;
    public MixPlan Plan => _plan;
    public double BContentIndex => _voiceB?.ContentIndex ?? double.NaN;
    public long NodeIndexB(long tapFrames) => double.IsNaN(MapB) ? -1 : (long)Math.Round(tapFrames + MapB);

    public void RequestAbort() => _pendingAbort = true;

    public void BeginForcedHandBack(double fadeSeconds)
    {
        _forcedFadeSeconds = fadeSeconds;
        _forceHandBack = true;
    }

    public void StartOffline(long regionIndexA)
    {
        _voiceA = new MixVoice(_regionA, _regionA.StartSeconds + regionIndexA / (double)_sampleRate, false);
        _voiceA.SetGainImmediate(_volumeA);
        Stage = AutoMixStage.Handover;
    }

    public void OnQuantum()
    {
        if (Stage is AutoMixStage.Done or AutoMixStage.Aborted) return;
        if (_pendingAbort)
        {
            Abort();
            return;
        }
        if (_forceHandBack || _fadeOutFrames > 0) return;
        if (Stage is AutoMixStage.RampBack or AutoMixStage.Aligning && _voiceB is not null &&
            _voiceB.ContentIndex > _regionB.Frames - _sampleRate * 1.5)
            NeedsForcedHandBack = true;

        switch (Stage)
        {
            case AutoMixStage.Waiting:
                if (TapA is null || double.IsNaN(MapA)) return;
                var nodeIndex = (long)Math.Round(TapA.TotalFrames + MapA);
                if (nodeIndex < _handoverIndexA) return;
                _voiceA = new MixVoice(_regionA, _regionA.StartSeconds + nodeIndex / (double)_sampleRate, false);
                _voiceA.SetGainImmediate(_volumeA);
                _player.SetVolume(_sourceA, 0);
                _pendingHandover = true;
                Stage = AutoMixStage.Handover;
                break;
            case AutoMixStage.Handover:
            case AutoMixStage.Overlap:
                if (BStarted && _nodeSpeedB != _voiceB!.Rate) SetNodeSpeed(_voiceB.Rate);
                break;
            case AutoMixStage.RampBack:
                if (_voiceB is null) break;
                var elapsed = (_rendered - _overlapEndRendered) / (double)_sampleRate;
                var t = _plan.RampBack <= 0 ? 1 : Math.Clamp(elapsed / _plan.RampBack, 0, 1);
                var smooth = t * t * (3 - 2 * t);
                _voiceB.Rate = _plan.Rate + (1 - _plan.Rate) * smooth;
                var gainT = Math.Clamp(elapsed / 2.0, 0, 1);
                _voiceB.TargetGain = _volumeB * (_plan.GainB + (1 - _plan.GainB) * gainT);
                SetNodeSpeed(_voiceB.Rate);
                if (t >= 1 && gainT >= 1)
                {
                    if (_voiceB.IsStretching) _voiceB.BeginRawBlend(0.05);
                    else if (double.IsNaN(MapB)) NeedsMapB = true;
                    else Stage = AutoMixStage.Aligning;
                }
                break;
            case AutoMixStage.Aligning:
                Align();
                break;
        }
    }

    private void SetNodeSpeed(double speed)
    {
        if (Math.Abs(speed - _nodeSpeedB) < 1e-6) return;
        _nodeSpeedB = speed;
        try
        {
            _player.SetSpeed(_sourceB, speed);
        }
        catch
        {
        }
    }

    private void Align()
    {
        if (_voiceB is null || TapB is null || double.IsNaN(MapB)) return;
        var quantum = _player.SamplesPerQuantum;
        if (double.IsNaN(_predictedNodeIndexB))
        {
            _predictedNodeIndexB = TapB.TotalFrames + MapB;
            SetNodeSpeed(1);
            _alignChased = Math.Abs(_predictedNodeIndexB - _voiceB.ContentIndex) > _sampleRate * 0.05;
        }
        else
        {
            _predictedNodeIndexB += quantum * _nodeSpeedB;
        }
        var delta = _predictedNodeIndexB - _voiceB.ContentIndex;
        if (Math.Abs(delta) < 0.5)
        {
            SetNodeSpeed(1);
            if (++_alignStableQuanta < 2) return;
            if (_alignChased && !_alignRefined)
            {
                _alignRefined = true;
                _alignStableQuanta = 0;
                _predictedNodeIndexB = double.NaN;
                MapB = double.NaN;
                Stage = AutoMixStage.RampBack;
                return;
            }
            _player.SetVolume(_sourceB, _nodeVolumeB);
            _pendingHandBack = true;
            return;
        }
        _alignStableQuanta = 0;
        var correction = Math.Clamp(-delta / (_sampleRate * 0.25), -0.5, 1);
        var maxStep = Math.Abs(delta) / quantum;
        if (Math.Abs(correction) > maxStep) correction = Math.Sign(correction) * maxStep;
        SetNodeSpeed(1 + correction);
    }

    private void Abort()
    {
        Stage = AutoMixStage.Aborted;
        try
        {
            if (_voiceA is not null && !OutgoingFinished && !MidpointReached)
                _player.SetVolume(_sourceA, _nodeVolumeA);
            if (_voiceB is not null && MidpointReached)
            {
                SetNodeSpeed(1);
                _player.SetVolume(_sourceB, _nodeVolumeB);
            }
        }
        catch
        {
        }
    }

    public void Render(Span<float> mix, int frames)
    {
        if (Stage == AutoMixStage.Aborted)
        {
            if (_voiceA is not null) { _voiceA.RenderRampOut(mix, frames, _clickRamp); _voiceA.Dispose(); _voiceA = null; }
            if (_voiceB is not null) { _voiceB.RenderRampOut(mix, frames, _clickRamp); _voiceB.Dispose(); _voiceB = null; }
            _rendered += frames;
            return;
        }
        if (_forceHandBack && _voiceB is not null && _fadeOutFrames == 0)
        {
            _forceHandBack = false;
            _fadeOutFrames = Math.Max(1, (int)(_forcedFadeSeconds * _sampleRate));
            _fadeOutDone = 0;
            _fadeOutBase = _voiceB.TargetGain;
        }
        if (_fadeOutFrames > 0)
        {
            _fadeOutDone += frames;
            var fade = Math.Clamp(1 - _fadeOutDone / (double)_fadeOutFrames, 0, 1);
            if (_voiceB is not null)
            {
                _voiceB.TargetGain = _fadeOutBase * fade;
                _voiceB.Render(mix, frames, 0);
                Limit(mix, frames);
            }
            _rendered += frames;
            if (fade <= 0 || _voiceB is null)
            {
                _voiceB?.Dispose();
                _voiceB = null;
                Stage = AutoMixStage.Done;
            }
            return;
        }
        if (_pendingHandBack && _voiceB is not null)
        {
            _voiceB.RenderRampOut(mix, frames, _clickRamp);
            _voiceB.Dispose();
            _voiceB = null;
            _pendingHandBack = false;
            Stage = AutoMixStage.Done;
            _rendered += frames;
            return;
        }

        var offset = 0;
        if (_voiceA is not null)
        {
            var aIndex = (long)Math.Round(_voiceA.ContentIndex);
            if (_voiceB is null && aIndex + frames > _exitIndexA)
            {
                var split = (int)Math.Clamp(_exitIndexA - aIndex, 0, frames);
                RenderA(mix, split, 0);
                StartB();
                offset = split;
            }
        }

        var remaining = frames - offset;
        if (remaining > 0)
        {
            var slice = mix.Slice(offset * 2, remaining * 2);
            UpdateOverlap();
            if (_voiceA is not null) RenderA(slice, remaining, 0);
            if (_voiceB is not null)
            {
                var ramp = 0;
                _voiceB.Render(slice, remaining, ramp);
            }
        }
        Limit(mix, frames);
        _rendered += frames;

        if (_voiceA is not null && (_voiceA.Finished || Progress >= 1 && _voiceA.TargetGain <= 0.0001))
        {
            _voiceA.Dispose();
            _voiceA = null;
            OutgoingFinished = true;
            if (_overlapEndRendered < 0) _overlapEndRendered = _rendered;
            if (Stage == AutoMixStage.Overlap) Stage = AutoMixStage.RampBack;
        }
    }

    private void Limit(Span<float> mix, int frames)
    {
        const double threshold = 0.97;
        var release = 1 - Math.Exp(-1.0 / (0.12 * _sampleRate));
        for (var i = 0; i < frames; i++)
        {
            var peak = Math.Max(Math.Abs(mix[i * 2]), Math.Abs(mix[i * 2 + 1]));
            var needed = peak * _limiterGain > threshold ? threshold / peak : 1;
            _limiterGain = needed < _limiterGain ? needed : _limiterGain + (1 - _limiterGain) * release;
            mix[i * 2] = (float)(mix[i * 2] * _limiterGain);
            mix[i * 2 + 1] = (float)(mix[i * 2 + 1] * _limiterGain);
        }
    }

    private void RenderA(Span<float> mix, int frames, int unused)
    {
        if (_voiceA is null || frames <= 0) return;
        var ramp = 0;
        if (_pendingHandover)
        {
            ramp = _clickRamp;
            _pendingHandover = false;
        }
        _voiceA.Render(mix, frames, ramp);
    }

    private void StartB()
    {
        if (_voiceB is not null) return;
        var stretch = Math.Abs(_plan.Rate - 1) > 0.0005;
        _voiceB = new MixVoice(_regionB, _plan.BEntry, stretch);
        _voiceB.Rate = _plan.Rate;
        _voiceB.SetGainImmediate(0);
        if (_plan.Technique == MixTechnique.BeatMix) _voiceB.LowShelfDb = -24;
        _startRenderedB = _rendered;
        BStarted = true;
        Stage = AutoMixStage.Overlap;
        try
        {
            SetNodeSpeed(_plan.Rate);
            _player.Play(_sourceB);
        }
        catch
        {
        }
    }

    private void UpdateOverlap()
    {
        if (_voiceA is null || !BStarted)
        {
            Progress = 0;
            return;
        }
        var x = Math.Clamp((_voiceA.ContentIndex - _exitIndexA) / _overlapFrames, 0, 1);
        Progress = x;
        if (x >= 0.5) MidpointReached = true;
        var gainB = _volumeB * _plan.GainB;
        var handoff = Math.Clamp(_plan.Handoff, 0, 0.95);
        var release = x < handoff ? 0 : (x - handoff) / (1 - handoff);
        switch (_plan.Technique)
        {
            case MixTechnique.BeatMix:
                _voiceA.TargetGain = _volumeA * Math.Cos(release * Math.PI / 2);
                if (_voiceB is not null) _voiceB.TargetGain = gainB * (x < handoff ? Math.Sin(x / handoff * Math.PI / 2) : 1);
                if (x >= handoff)
                {
                    _voiceA.LowShelfDb = -24;
                    if (_voiceB is not null) _voiceB.LowShelfDb = 0;
                }
                break;
            case MixTechnique.FilterCut:
                _voiceA.HighPassHz = 300 * Math.Pow(60, x);
                _voiceA.TargetGain = _volumeA * (x < 0.7 ? 1 : Math.Cos((x - 0.7) / 0.3 * Math.PI / 2));
                if (_voiceB is not null) _voiceB.TargetGain = gainB * (x < 0.5 ? 0 : Math.Sin((x - 0.5) / 0.5 * Math.PI / 2));
                break;
            default:
                if (handoff < 0.1)
                {
                    _voiceA.TargetGain = _volumeA * Math.Cos(x * Math.PI / 2);
                    if (_voiceB is not null) _voiceB.TargetGain = gainB * Math.Sin(x * Math.PI / 2);
                    break;
                }
                _voiceA.TargetGain = _volumeA * Math.Cos(release * Math.PI / 2);
                if (_voiceB is not null)
                    _voiceB.TargetGain = gainB * (x < handoff
                        ? 0.6 * Math.Sin(x / handoff * Math.PI / 2)
                        : 0.6 + 0.4 * Math.Sin(release * Math.PI / 2));
                break;
        }
        if (x >= 1) _voiceA.TargetGain = 0;
    }

    public void Dispose()
    {
        _voiceA?.Dispose();
        _voiceB?.Dispose();
        _voiceA = null;
        _voiceB = null;
    }
}
