using System;
using System.Threading;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.Features.Playback.AutoMix;

public interface IAutoMixHost
{
    void SetVolume(AudioGraphPlaybackSource source, double volume);
    void SetSpeed(AudioGraphPlaybackSource source, double speed);
    void Play(AudioGraphPlaybackSource source);
    void Seek(AudioGraphPlaybackSource source, TimeSpan position);
}

public sealed class GraphMixHost : IAutoMixHost
{
    private readonly AudioGraphPlayer _graph;

    public GraphMixHost(AudioGraphPlayer graph) => _graph = graph;

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

public sealed class AutoMixEngine : IMixPumpSource, IDisposable
{
    public const double CrossfadeSeconds = 0.2;
    public const double AbortFadeSeconds = 0.01;
    public const double TailDuck = 0.25;
    private const double TailBloomLead = 0.15;
    private const double TailBloomSeconds = 0.2;
    private const int HistoryLength = 64;

    private readonly IAutoMixHost _player;
    private readonly MixPlan _plan;
    private readonly MixRegion _regionA;
    private readonly MixRegion _regionB;
    private readonly AudioGraphPlaybackSource _sourceA;
    private readonly AudioGraphPlaybackSource _sourceB;
    private readonly int _sampleRate;
    private readonly double _volumeA;
    private readonly double _volumeB;
    private readonly double _nodeVolumeA;
    private readonly double _nodeVolumeB;
    private readonly long _exitIndexA;
    private readonly long _overlapFrames;
    private readonly long _handoverIndexA;
    private readonly long _entryFadeFrames;
    private readonly long _cutIndexA;
    private readonly long _tailFrames;
    private readonly ClockOffsetEstimator _clockA = new();
    private readonly ClockOffsetEstimator _clockB = new();
    private readonly long[] _historyRendered = new long[HistoryLength];
    private readonly double[] _historyContent = new double[HistoryLength];
    private readonly double[] _historyRate = new double[HistoryLength];

    private MixVoice? _voiceA;
    private MixVoice? _voiceB;
    private MixVoice? _tail;
    private NodeCrossfade? _fadeA;
    private NodeCrossfade? _fadeB;
    private PlaybackTap? _clockTapA;
    private PlaybackTap? _clockTapB;
    private float[] _envelope = new float[1024];
    private AutoMixStage _renderStage = AutoMixStage.Waiting;
    private volatile bool _pendingAbort;
    private volatile bool _forceHandBack;
    private double _forcedFadeSeconds;
    private double _appliedA;
    private double _appliedB;
    private long _capturedA = -1;
    private long _capturedB = -1;
    private long _rendered;
    private long _played;
    private long _handoverStart = -1;
    private long _startRenderedB = -1;
    private long _overlapOrigin;
    private long _overlapEndRendered = -1;
    private long _rampBackEndRendered = -1;
    private long _doneAt = -1;
    private long _voiceOffsetB;
    private long _tailRendered;
    private double _tailContent = double.NaN;
    private double _tailRate = 1;
    private double _renderProgress;
    private double _playedContentB = double.NaN;
    private double _nodeSpeedB = double.NaN;
    private double _chaseDelta;
    private long _chasePlayed;
    private bool _chasing;
    private bool _alignMeasured;
    private bool _alignChased;
    private bool _alignRefined;
    private double _limiterGain = 1;
    private int _historyCount;
    private int _historyNext;
    private int _cutFrames = -1;
    private bool _cutA;

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
        _volumeA = volumeA;
        _volumeB = volumeB;
        _nodeVolumeA = nodeVolumeA;
        _nodeVolumeB = nodeVolumeB;
        _exitIndexA = regionA.IndexOf(plan.AExit);
        _overlapFrames = Math.Max(1, (long)Math.Round(plan.Overlap * _sampleRate));
        _handoverIndexA = Math.Max(0, _exitIndexA - _sampleRate * 2);
        _entryFadeFrames = plan.EntryFade > 0 ? Math.Max(1, (long)Math.Round(plan.EntryFade * _sampleRate)) : 0;
        _cutIndexA = _exitIndexA + _overlapFrames;
        _tailFrames = Math.Max(1, (long)Math.Round(plan.TailLength * _sampleRate));
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
    public double BContentIndex => Volatile.Read(ref _playedContentB);
    public double BContentSeconds => _regionB.StartSeconds + BContentIndex / _sampleRate;
    public double SecondsSinceBStarted => BStarted ? Math.Max(0, Volatile.Read(ref _played) - Volatile.Read(ref _startRenderedB)) / (double)_sampleRate : 0;
    public double NodeVolumeA => _nodeVolumeA;
    public double NodeVolumeB => _nodeVolumeB;
    public double Progress { get; private set; }
    public long HandoverIndexA => _handoverIndexA;
    public MixPlan Plan => _plan;
    public bool HandOverCrossfaded { get; private set; }
    public bool HandBackCrossfaded { get; private set; }
    public bool HandBackForced { get; private set; }
    public int HandBackChases { get; private set; }
    public double HandBackMeasured { get; private set; } = double.NaN;

    public void RequestAbort() => _pendingAbort = true;

    public void BeginForcedHandBack(double fadeSeconds)
    {
        _forcedFadeSeconds = fadeSeconds;
        _forceHandBack = true;
    }

    public void StartOffline(long regionIndexA)
    {
        _voiceA = CreateVoiceA(_regionA.StartSeconds + regionIndexA / (double)_sampleRate);
        _renderStage = AutoMixStage.Handover;
        Stage = AutoMixStage.Handover;
    }

    public void Capture()
    {
        _capturedA = Capture(TapA, ref _clockTapA, _clockA);
        _capturedB = Capture(TapB, ref _clockTapB, _clockB);
    }

    private static long Capture(PlaybackTap? tap, ref PlaybackTap? known, ClockOffsetEstimator clock)
    {
        if (!ReferenceEquals(tap, known))
        {
            known = tap;
            clock.Reset();
        }

        return tap is not null && tap.Pull() ? tap.TotalFrames : -1;
    }

    public void Advance(in MixClock clock)
    {
        var played = clock.Played;
        Volatile.Write(ref _played, played);
        if (Stage is AutoMixStage.Done or AutoMixStage.Aborted) return;
        if (_pendingAbort)
        {
            Abort(true);
            return;
        }

        if (_forceHandBack)
        {
            _forceHandBack = false;
            if (_voiceB is not null)
            {
                var fade = Math.Max(1, (int)(_forcedFadeSeconds * _sampleRate));
                HandBackForced = true;
                _cutFrames = fade;
                _fadeA = null;
                _fadeB = null;
                _chasing = false;
                ReleaseVoices();
                _renderStage = AutoMixStage.Done;
                _doneAt = played + fade;
                return;
            }
        }

        if (clock.Stable)
        {
            if (_capturedA >= 0) _clockA.Add(_capturedA - played);
            if (_capturedB >= 0) _clockB.Add(_capturedB - played);
        }

        if (ApplyFade(_fadeA, _sourceA, _nodeVolumeA, ref _appliedA, clock)) _fadeA = null;
        if (ApplyFade(_fadeB, _sourceB, _nodeVolumeB, ref _appliedB, clock)) _fadeB = null;

        if (_doneAt >= 0)
        {
            if (played >= _doneAt && _fadeB is null) Stage = AutoMixStage.Done;
            return;
        }

        if (Stage == AutoMixStage.Waiting && _handoverStart >= 0 && played >= _handoverStart)
            Stage = AutoMixStage.Handover;

        if (!BStarted && _startRenderedB >= 0 && played >= _startRenderedB)
        {
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

        if (BStarted)
        {
            Volatile.Write(ref _playedContentB, ContentAt(played, out var rate));
            if (!OutgoingFinished)
            {
                var progress = Math.Clamp((played - _overlapOrigin) / (double)_overlapFrames, 0, 1);
                Progress = progress;
                if (progress >= 0.5) MidpointReached = true;
            }

            if (Stage is AutoMixStage.Overlap or AutoMixStage.RampBack && !_chasing) SetNodeSpeed(rate);
        }

        if (!OutgoingFinished && _overlapEndRendered >= 0 && played >= _overlapEndRendered)
        {
            OutgoingFinished = true;
            Progress = 0;
            if (Stage == AutoMixStage.Overlap) Stage = AutoMixStage.RampBack;
        }

        if (Stage == AutoMixStage.RampBack && _rampBackEndRendered >= 0 && played >= _rampBackEndRendered)
        {
            if (double.IsNaN(MapB)) NeedsMapB = true;
            else Stage = AutoMixStage.Aligning;
        }

        if (Stage == AutoMixStage.Aligning) Align(clock);
        if (_renderStage == AutoMixStage.Waiting) ScheduleHandover(clock);
    }

    private bool ApplyFade(NodeCrossfade? fade, AudioGraphPlaybackSource source, double volume, ref double applied, in MixClock clock)
    {
        if (fade is null) return false;
        var finished = clock.Played >= fade.End;
        if (!clock.Stable && !finished) return false;
        var target = volume * fade.NodeTarget(clock.Played);
        if (Math.Abs(target - applied) > 1e-9 || finished)
        {
            applied = target;
            try
            {
                _player.SetVolume(source, target);
            }
            catch
            {
            }
        }

        return finished;
    }

    private void ScheduleHandover(in MixClock clock)
    {
        if (!clock.Stable || TapA is null || double.IsNaN(MapA) || !_clockA.TryGet(out var offset)) return;
        var shift = (long)Math.Round(MapA) + offset;
        var start = NodeCrossfade.AlignUp(_rendered, clock.Played, clock.Quantum);
        if (start + shift < _handoverIndexA) return;
        _voiceA = CreateVoiceA(_regionA.StartSeconds + (_rendered + shift) / (double)_sampleRate);
        _fadeA = new NodeCrossfade(start, clock.Quantum, FadeSteps(clock.Quantum), _sampleRate, false);
        _appliedA = _nodeVolumeA;
        _handoverStart = start;
        _renderStage = AutoMixStage.Handover;
        HandOverCrossfaded = true;
    }

    private MixVoice CreateVoiceA(double contentSeconds)
    {
        var voice = new MixVoice(_regionA, contentSeconds, false);
        voice.SetGainImmediate(_volumeA);
        var wet = _volumeA * (_plan.TailBloom > 0 ? TailDuck : 1);
        if (_plan.Tail == MixTail.Reverb) voice.EnableSend(MixSendSettings.ReverbTail(_plan.TailDelay), wet);
        else if (_plan.Tail == MixTail.Echo) voice.EnableSend(MixSendSettings.EchoOut(_plan.TailDelay), wet);
        return voice;
    }

    private int FadeSteps(int quantum) => Math.Max(1, (int)Math.Round(CrossfadeSeconds * _sampleRate / Math.Max(1, quantum)));

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

    private void Align(in MixClock clock)
    {
        if (_voiceB is null || _fadeB is not null || double.IsNaN(MapB) || !clock.Stable) return;
        if (!_chasing)
        {
            if (!_clockB.TryGet(out var offset)) return;
            var delta = MapB + offset - _voiceOffsetB;
            SetNodeSpeed(1);
            if (!_alignMeasured)
            {
                _alignMeasured = true;
                _alignChased = Math.Abs(delta) > _sampleRate * 0.05;
            }

            HandBackMeasured = delta;
            if (Math.Abs(delta) >= 0.5) HandBackChases++;
            _chasing = true;
            _chaseDelta = delta;
        }
        else
        {
            _chaseDelta += (clock.Played - _chasePlayed) * (_nodeSpeedB - 1);
        }

        _chasePlayed = clock.Played;
        if (Math.Abs(_chaseDelta) < 0.5)
        {
            SetNodeSpeed(1);
            _chasing = false;
            if (_alignChased && !_alignRefined)
            {
                _alignRefined = true;
                _alignMeasured = false;
                MapB = double.NaN;
                Stage = AutoMixStage.RampBack;
                return;
            }

            var start = NodeCrossfade.AlignUp(_rendered, clock.Played, clock.Quantum);
            _fadeB = new NodeCrossfade(start, clock.Quantum, FadeSteps(clock.Quantum), _sampleRate, true);
            _appliedB = 0;
            _doneAt = _fadeB.End;
            HandBackCrossfaded = true;
            return;
        }

        SetNodeSpeed(1 + Math.Clamp(-_chaseDelta / (_sampleRate * 0.25), -0.5, 1));
    }

    private void Abort(bool cut)
    {
        Stage = AutoMixStage.Aborted;
        _renderStage = AutoMixStage.Aborted;
        _fadeA = null;
        _fadeB = null;
        _chasing = false;
        if (cut) _cutFrames = Math.Max(1, (int)(AbortFadeSeconds * _sampleRate));
        try
        {
            if (_handoverStart >= 0 && !OutgoingFinished && !MidpointReached)
                _player.SetVolume(_sourceA, _nodeVolumeA);
            if (BStarted && MidpointReached)
            {
                SetNodeSpeed(1);
                _player.SetVolume(_sourceB, _nodeVolumeB);
            }
        }
        catch
        {
        }

        ReleaseVoices();
    }

    public void Fail()
    {
        if (Stage is AutoMixStage.Done or AutoMixStage.Aborted) return;
        Abort(false);
    }

    public bool TakeCut(out int fadeFrames)
    {
        fadeFrames = _cutFrames;
        if (_cutFrames < 0) return false;
        _cutFrames = -1;
        return true;
    }

    public void Render(Span<float> mix, int frames)
    {
        if (frames <= 0) return;
        if (_renderStage is AutoMixStage.Done or AutoMixStage.Aborted)
        {
            _rendered += frames;
            return;
        }

        PrepareBlock();
        var offset = 0;
        if (_voiceA is not null)
        {
            var aIndex = (long)Math.Round(_voiceA.ContentIndex);
            if (_voiceB is null && aIndex + frames > _exitIndexA)
            {
                var split = (int)Math.Clamp(_exitIndexA - aIndex, 0, frames);
                UpdateSend(split);
                RenderA(mix, split, 0);
                StartB(split);
                offset = split;
            }
        }

        var remaining = frames - offset;
        if (remaining > 0)
        {
            var slice = mix.Slice(offset * 2, remaining * 2);
            UpdateOverlap();
            UpdateSend(remaining);
            RenderA(slice, remaining, offset);
            RenderB(slice, remaining, offset);
            _tail?.RenderTail(slice, remaining);
        }

        Limit(mix, frames);
        _rendered += frames;
        if (_voiceB is not null)
        {
            _tailRendered = _rendered;
            _tailContent = _voiceB.ContentIndex;
            _tailRate = _voiceB.Rate;
        }

        if (_voiceA is not null && (_voiceA.Finished || (_renderProgress >= 1 || _cutA) && _voiceA.TargetGain <= 0.0001))
        {
            if (_voiceA.TailActive)
            {
                _tail = _voiceA;
                _tail.WetGain = _volumeA;
                _tail.BeginTail(_tailFrames);
            }
            else
            {
                _voiceA.Dispose();
            }

            _voiceA = null;
            if (_overlapEndRendered < 0) _overlapEndRendered = _rendered;
            if (_renderStage == AutoMixStage.Overlap) _renderStage = AutoMixStage.RampBack;
        }

        if (_tail is not null && !_tail.TailActive)
        {
            _tail.Dispose();
            _tail = null;
        }

        if (_voiceB is not null && _fadeB is { } fade && _rendered >= fade.End)
        {
            _voiceB.Dispose();
            _voiceB = null;
            _tail?.Dispose();
            _tail = null;
            _renderStage = AutoMixStage.Done;
        }
    }

    private void PrepareBlock()
    {
        if (_renderStage != AutoMixStage.RampBack || _voiceB is null) return;
        if (_voiceB.ContentIndex > _regionB.Frames - _sampleRate * 1.5) NeedsForcedHandBack = true;
        if (_rampBackEndRendered >= 0) return;
        var elapsed = (_rendered - _overlapEndRendered) / (double)_sampleRate;
        var t = _plan.RampBack <= 0 ? 1 : Math.Clamp(elapsed / _plan.RampBack, 0, 1);
        var smooth = t * t * (3 - 2 * t);
        _voiceB.Rate = _plan.Rate + (1 - _plan.Rate) * smooth;
        var gainT = Math.Clamp(elapsed / Math.Max(0.05, _plan.GainRelease), 0, 1);
        var ease = EntryEase();
        _voiceB.TargetGain = _volumeB * (_plan.GainB + (1 - _plan.GainB) * gainT) * ease;
        if (t < 1 || gainT < 1 || ease < 1 || _tail is not null) return;
        if (_voiceB.IsStretching)
        {
            _voiceB.BeginRawBlend(0.05);
            return;
        }

        _rampBackEndRendered = _rendered;
        _voiceOffsetB = (long)Math.Round(_voiceB.ContentIndex) - _rendered;
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

    private void RenderA(Span<float> mix, int frames, int offset)
    {
        if (_voiceA is null || frames <= 0) return;
        _voiceA.Render(mix, frames, Envelope(_fadeA, _rendered + offset, frames));
    }

    private void RenderB(Span<float> mix, int frames, int offset)
    {
        if (_voiceB is null || frames <= 0) return;
        _historyRendered[_historyNext] = _rendered + offset;
        _historyContent[_historyNext] = _voiceB.ContentIndex;
        _historyRate[_historyNext] = _voiceB.Rate;
        _historyNext = (_historyNext + 1) % HistoryLength;
        if (_historyCount < HistoryLength) _historyCount++;
        _voiceB.Render(mix, frames, Envelope(_fadeB, _rendered + offset, frames));
    }

    private ReadOnlySpan<float> Envelope(NodeCrossfade? fade, long start, int frames)
    {
        if (fade is null || start >= fade.End) return default;
        if (_envelope.Length < frames) _envelope = new float[frames];
        for (var i = 0; i < frames; i++) _envelope[i] = (float)fade.VoiceWeightAt(start + i);
        return new ReadOnlySpan<float>(_envelope, 0, frames);
    }

    private double ContentAt(long played, out double rate)
    {
        rate = _tailRate;
        if (_historyCount == 0) return double.NaN;
        if (played >= _tailRendered) return _tailContent;
        var nextRendered = _tailRendered;
        var nextContent = _tailContent;
        for (var i = 1; i <= _historyCount; i++)
        {
            var slot = (_historyNext - i + HistoryLength) % HistoryLength;
            var rendered = _historyRendered[slot];
            var content = _historyContent[slot];
            if (played >= rendered)
            {
                rate = _historyRate[slot];
                var span = nextRendered - rendered;
                return span <= 0 ? content : content + (nextContent - content) * (played - rendered) / span;
            }

            nextRendered = rendered;
            nextContent = content;
        }

        rate = _historyRate[(_historyNext - _historyCount + HistoryLength) % HistoryLength];
        return nextContent;
    }

    private void UpdateSend(int frames)
    {
        if (_voiceA is null || _plan.Tail == MixTail.None) return;
        var end = _voiceA.ContentSeconds + frames / (double)_sampleRate;
        var t = Math.Clamp((end - _plan.TailOpen) / Math.Max(1e-3, _plan.TailFull - _plan.TailOpen), 0, 1);
        _voiceA.SendGain = t * t * (3 - 2 * t);
        if (_plan.TailBloom > 0)
        {
            var bloom = Math.Clamp((end - (_plan.TailBloom - TailBloomLead)) / TailBloomSeconds, 0, 1);
            _voiceA.WetGain = _volumeA * (TailDuck + (1 - TailDuck) * bloom);
        }
        if (_plan.Tail != MixTail.Echo || _voiceB is null || _voiceA.ContentIndex + 2 * frames < _cutIndexA) return;
        _cutA = true;
        _voiceA.SendGain = 0;
        _voiceA.TargetGain = 0;
    }

    private double EntryEase()
    {
        if (_entryFadeFrames <= 0) return 1;
        var t = Math.Clamp((_rendered - Volatile.Read(ref _startRenderedB)) / (double)_entryFadeFrames, 0, 1);
        return 0.5 - 0.5 * Math.Cos(Math.PI * t);
    }

    private void StartB(int split)
    {
        if (_voiceB is not null) return;
        var stretch = Math.Abs(_plan.Rate - 1) > 0.0005;
        _voiceB = new MixVoice(_regionB, _plan.BEntry, stretch);
        _voiceB.Rate = _plan.Rate;
        _voiceB.SetGainImmediate(0);
        if (_plan.Technique == MixTechnique.BeatMix) _voiceB.LowShelfDb = -24;
        var started = _rendered + split;
        _overlapOrigin = started - (long)Math.Round((_voiceA?.ContentIndex ?? _exitIndexA) - _exitIndexA);
        _tailRendered = started;
        _tailContent = _voiceB.ContentIndex;
        _tailRate = _voiceB.Rate;
        _renderStage = AutoMixStage.Overlap;
        Volatile.Write(ref _startRenderedB, started);
    }

    private void UpdateOverlap()
    {
        if (_voiceA is null || _voiceB is null)
        {
            _renderProgress = 0;
            return;
        }
        var x = Math.Clamp((_voiceA.ContentIndex - _exitIndexA) / _overlapFrames, 0, 1);
        _renderProgress = x;
        var gainB = _volumeB * _plan.GainB;
        var handoff = Math.Clamp(_plan.Handoff, 0, 0.95);
        var release = x < handoff ? 0 : (x - handoff) / (1 - handoff);
        switch (_plan.Technique)
        {
            case MixTechnique.BeatMix:
                _voiceA.TargetGain = _volumeA * Math.Cos(release * Math.PI / 2);
                _voiceB.TargetGain = gainB * (x < handoff ? Math.Sin(x / handoff * Math.PI / 2) : 1);
                if (x >= handoff)
                {
                    _voiceA.LowShelfDb = -24;
                    _voiceB.LowShelfDb = 0;
                }
                break;
            case MixTechnique.FilterCut:
                if (_plan.Tail == MixTail.Echo)
                {
                    _voiceA.HighPassHz = 300 * Math.Pow(8.0 / 3, x);
                    _voiceA.TargetGain = _volumeA * (x < 0.5 ? 1 : 0.8 + 0.2 * Math.Cos((x - 0.5) / 0.5 * Math.PI));
                    _voiceB.TargetGain = gainB * (x < 0.4 ? 0 : x < 0.9 ? Math.Sin((x - 0.4) / 0.5 * Math.PI / 2) : 1);
                    break;
                }
                _voiceA.HighPassHz = 300 * Math.Pow(60, x);
                _voiceA.TargetGain = _volumeA * (x < 0.7 ? 1 : Math.Cos((x - 0.7) / 0.3 * Math.PI / 2));
                _voiceB.TargetGain = gainB * (x < 0.5 ? 0 : Math.Sin((x - 0.5) / 0.5 * Math.PI / 2));
                break;
            default:
                if (_entryFadeFrames > 0)
                {
                    _voiceA.TargetGain = _volumeA;
                    _voiceB.TargetGain = gainB * EntryEase();
                    break;
                }
                if (handoff < 0.1)
                {
                    _voiceA.TargetGain = _volumeA * Math.Cos(x * Math.PI / 2);
                    _voiceB.TargetGain = gainB * Math.Sin(x * Math.PI / 2);
                    break;
                }
                _voiceA.TargetGain = _volumeA * Math.Cos(release * Math.PI / 2);
                _voiceB.TargetGain = gainB * (x < handoff
                    ? 0.6 * Math.Sin(x / handoff * Math.PI / 2)
                    : 0.6 + 0.4 * Math.Sin(release * Math.PI / 2));
                break;
        }
        if (x >= 1) _voiceA.TargetGain = 0;
    }

    private void ReleaseVoices()
    {
        _voiceA?.Dispose();
        _voiceB?.Dispose();
        _tail?.Dispose();
        _voiceA = null;
        _voiceB = null;
        _tail = null;
    }

    public void Dispose() => ReleaseVoices();
}
