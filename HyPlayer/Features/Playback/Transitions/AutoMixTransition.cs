using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.Analysis;
using HyPlayer.Features.Playback.Analysis.Dsp;
using HyPlayer.Features.Playback.AutoMix;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.PlayCore.Abstraction.Models.AudioServiceComponents;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.UWP.Chopin.Dsp;

namespace HyPlayer.Features.Playback.Transitions;

public sealed class AutoMixTransition : ITrackTransition, IDisposable
{
    private static readonly TimeSpan _preloadLead = TimeSpan.FromSeconds(80);
    private static readonly TimeSpan _seekMargin = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _catchUpTime = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _restoreTime = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _forcedHandBackTime = TimeSpan.FromMilliseconds(60);
    private const double RetryLeadSeconds = 6;
    private const double AnalysisStartSeconds = 12;
    private const double AnalysisUrgentRemainingSeconds = 150;

    private readonly AutoMixSettings _settings;
    private readonly IPlayer _player;
    private readonly IAudioAnalysisService _analysis;
    private readonly ILyricService _lyrics;
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly NextTrackPreloader _preloader = new();
    private readonly CrossFadeTransition _fallback;
    private readonly MixRetryBudget _retries = new();

    private IPlaybackSource? _analyzedFor;
    private TransitionPreparedTrack? _plannedFor;
    private Task<TrackAnalysis?>? _analysisA;
    private Task<TrackAnalysis?>? _analysisB;
    private Task<VocalRegions>? _vocalsA;
    private Task<VocalRegions>? _vocalsB;
    private DateTime _lastAnalysisAttempt;
    private CancellationTokenSource _cts = new();

    private SingleSongBase? _aheadFor;
    private Task<bool>? _aheadTask;
    private DateTime _aheadAttempt;
    private DateTime _aheadPeek;

    private MixPlan? _plan;
    private bool _planHadVocals;
    private volatile bool _planBlocked;
    private volatile bool _reloadIncoming;
    private volatile bool _replan;
    private volatile IPlaybackSource? _blockedFor;
    private SingleSongBase? _reloadedIncoming;
    private long _decodeRetryAfter;
    private int _decoding;
    private int _epoch;
    private PendingMix? _pending;
    private MixSession? _session;
    private bool _disposed;

    public AutoMixTransition(
        AutoMixSettings settings,
        IPlayer player,
        IAudioAnalysisService analysis,
        ILyricService lyrics,
        IBackgroundTaskRunner taskRunner)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        _lyrics = lyrics ?? throw new ArgumentNullException(nameof(lyrics));
        _taskRunner = taskRunner ?? throw new ArgumentNullException(nameof(taskRunner));
        _fallback = new CrossFadeTransition(_preloader);
    }

    public string Id => "mix";

    private AudioGraphPlayer Graph => (AudioGraphPlayer)_player;

    public async Task OnPositionChangedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (Volatile.Read(ref _session) is { } session)
        {
            await SuperviseAsync(session, context, ct).ConfigureAwait(false);
            return;
        }

        if (_fallback.IsActive)
        {
            await _fallback.OnPositionChangedAsync(context, ct).ConfigureAwait(false);
            return;
        }

        var current = context.Song;
        if (current is null)
            return;

        if (!ReferenceEquals(_analyzedFor, context.Source))
        {
            ResetForTrack();
            _analyzedFor = context.Source;
        }

        if (_reloadIncoming)
        {
            _reloadIncoming = false;
            await _preloader.DiscardAsync().ConfigureAwait(false);
        }

        if (_replan)
        {
            _replan = false;
            _plan = null;
        }

        await _preloader.PrepareAsync(context, _preloadLead, ct).ConfigureAwait(false);
        var prepared = _preloader.Prepared;
        if (!ReferenceEquals(prepared, _plannedFor))
        {
            _plannedFor = prepared;
            _analysisB = null;
            _vocalsB = null;
            _plan = null;
            _planBlocked = ReferenceEquals(_blockedFor, context.Source);
            Volatile.Write(ref _pending, null);
        }

        var remaining = context.Duration - context.Position;
        await RequestAnalysesAsync(context, current, prepared, remaining, ct).ConfigureAwait(false);

        if (Interlocked.Exchange(ref _pending, null) is { } pending && TryInstall(context, pending))
            return;

        var decoding = Volatile.Read(ref _decoding) != 0;
        if (_planBlocked)
            _plan = null;
        else if (!decoding)
            UpdatePlan(context, current, prepared, remaining);

        if (_plan is null && !decoding)
            await _fallback.OnPositionChangedAsync(context, ct).ConfigureAwait(false);
    }

    public async Task OnTrackCompletedAsync(TrackTransitionContext context, CancellationToken ct)
    {
        if (Volatile.Read(ref _session) is { } session)
        {
            if (session.Promotion is not null)
            {
                await AbortAsync(session).ConfigureAwait(false);
                await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
                return;
            }

            if (session.Engine.Stage == AutoMixStage.Waiting)
            {
                AutoMixLog.Write("track ended before hand-over, starting next track directly");
                session.KeepPrepared = true;
                await AbortAsync(session).ConfigureAwait(false);
                try
                {
                    Graph.SeekPlaybackSource(TimeSpan.Zero, session.Incoming);
                }
                catch
                {
                }

                await StartPreparedAsync(context, session.Prepared, ct).ConfigureAwait(false);
                return;
            }

            await CommitAsync(session, ct).ConfigureAwait(false);
            if (session.Promotion is not null)
                return;

            await AbortAsync(session).ConfigureAwait(false);
            await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
            return;
        }

        Interlocked.Increment(ref _epoch);
        Volatile.Write(ref _pending, null);
        await _fallback.OnTrackCompletedAsync(context, ct).ConfigureAwait(false);
    }

    public async Task OnSeekAsync(TrackTransitionContext context, TimeSpan target, CancellationToken ct)
    {
        if (Volatile.Read(ref _session) is { } session)
        {
            Interlocked.Increment(ref _epoch);
            Volatile.Write(ref _pending, null);
            await AbortAsync(session).ConfigureAwait(false);
            _plan = null;
            _planBlocked = false;
            _blockedFor = null;
            return;
        }

        if (_fallback.IsActive)
        {
            await _fallback.OnSeekAsync(context, target, ct).ConfigureAwait(false);
            return;
        }

        if (target >= context.Duration - _preloadLead - _seekMargin)
            return;

        Interlocked.Increment(ref _epoch);
        Volatile.Write(ref _pending, null);
        await _preloader.DiscardAsync().ConfigureAwait(false);
        _plan = null;
        _plannedFor = null;
        _analysisB = null;
        _vocalsB = null;
    }

    public async Task OnQueueChangedAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _session) is { Promotion: not null })
            return;

        if (Volatile.Read(ref _session) is null && _fallback.IsActive)
        {
            await _fallback.OnQueueChangedAsync(ct).ConfigureAwait(false);
            return;
        }

        await CancelAsync(ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _epoch);
        Volatile.Write(ref _pending, null);
        if (Volatile.Read(ref _session) is { } session)
            await AbortAsync(session).ConfigureAwait(false);

        await _fallback.CancelAsync(ct).ConfigureAwait(false);
        _plan = null;
        _planBlocked = false;
        _blockedFor = null;
        _reloadIncoming = false;
        _plannedFor = null;
        _analysisB = null;
        _vocalsB = null;
    }

    private async Task RequestAnalysesAsync(
        TrackTransitionContext context,
        SingleSongBase current,
        TransitionPreparedTrack? prepared,
        TimeSpan remaining,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var token = _cts.Token;
        var retry = (now - _lastAnalysisAttempt).TotalSeconds > 8;
        var settled = context.Position.TotalSeconds >= AnalysisStartSeconds
                      || remaining.TotalSeconds < AnalysisUrgentRemainingSeconds
                      || _analysis.TryGetCached(current) is not null;
        if ((_analysisA is null || _analysisA.IsCompleted && _analysisA.Result is null && retry) && remaining.TotalSeconds > 30 && settled)
        {
            _lastAnalysisAttempt = now;
            _analysisA = _analysis.AnalyzeAsync(current, token);
        }

        if (_vocalsA is null && (context.Position.TotalSeconds > 30 || remaining.TotalSeconds < 120))
            _vocalsA = SafeVocals(current, true, token);

        if (prepared is null)
        {
            if (context.Position.TotalSeconds > 20 && remaining.TotalSeconds > 90 && _analysisA is { IsCompleted: true })
                await AnalyzeNextAheadAsync(context, now, ct).ConfigureAwait(false);
            return;
        }

        if (_analysisB is null || _analysisB.IsCompleted && _analysisB.Result is null && retry)
        {
            _lastAnalysisAttempt = now;
            _analysisB = _analysis.AnalyzeAsync(prepared.Song, token);
        }

        _vocalsB ??= SafeVocals(prepared.Song, false, token);
    }

    private async Task AnalyzeNextAheadAsync(TrackTransitionContext context, DateTime now, CancellationToken ct)
    {
        if (_aheadTask is { IsCompleted: false } || (now - _aheadPeek).TotalSeconds < 5)
            return;

        _aheadPeek = now;
        var next = await context.Host.PeekNextAsync(context, ct).ConfigureAwait(false);
        if (next is null || ReferenceEquals(next, context.Song))
            return;

        var same = ReferenceEquals(next, _aheadFor);
        if (same && _aheadTask is { IsCompletedSuccessfully: true, Result: true })
            return;
        if (same && (now - _aheadAttempt).TotalSeconds < 20)
            return;

        _aheadFor = next;
        _aheadAttempt = now;
        _aheadTask = AnalyzeAheadAsync(next, _cts.Token);
    }

    private async Task<bool> AnalyzeAheadAsync(SingleSongBase next, CancellationToken ct)
    {
        try
        {
            var analysis = await _analysis.AnalyzeAsync(next, ct).ConfigureAwait(false);
            if (analysis is null)
                return false;

            await _analysis.AnalyzeVocalsAsync(next, false, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<VocalRegions> SafeVocals(SingleSongBase item, bool outro, CancellationToken ct)
    {
        IReadOnlyList<(double Start, double End)> lyric;
        try
        {
            lyric = await _lyrics.GetVocalRegionsAsync(item, ct).ConfigureAwait(false);
        }
        catch
        {
            lyric = Array.Empty<(double, double)>();
        }

        VocalActivity? activity = null;
        try
        {
            activity = await _analysis.AnalyzeVocalsAsync(item, outro, ct).ConfigureAwait(false);
        }
        catch
        {
        }

        var regions = VocalActivity.Resolve(lyric, activity);
        if (activity is not null)
        {
            var lyricInWindow = lyric.Count(r => r.End > activity.Start && r.Start < activity.End);
            var inWindow = regions.Vocals.Where(r => r.End > activity.Start && r.Start < activity.End);
            AutoMixLog.Write(activity.IsLeadActivity
                ? $"vocals {(outro ? "outro" : "intro")} {item.Name} {activity.Start:0.0}-{activity.End:0.0}s: lyrics {lyricInWindow} lines, mid-band {Describe(activity.Regions(MidBandActivity.EnterThreshold, MidBandActivity.ExitThreshold))} -> vocals {Describe(inWindow)} leads {Describe(regions.Leads)}"
                : $"vocals {(outro ? "outro" : "intro")} {item.Name} {activity.Start:0.0}-{activity.End:0.0}s: lyrics {lyricInWindow} lines, model {Describe(activity.Regions(0.12, 0.06))} -> {Describe(inWindow)}");
        }

        return regions;
    }

    private static string Describe(IEnumerable<(double Start, double End)> regions)
    {
        return string.Join(" ", regions.Select(r => $"[{r.Start:0.0}-{r.End:0.0}]"));
    }

    private void UpdatePlan(
        TrackTransitionContext context,
        SingleSongBase current,
        TransitionPreparedTrack? prepared,
        TimeSpan remaining)
    {
        if (_plan is not null && !_planHadVocals && _vocalsA is { IsCompleted: true } && _vocalsB is { IsCompleted: true })
            _plan = null;
        if (_plan is null && prepared is not null)
            TryPlan(context, current, prepared, remaining);
        if (_plan is not { } plan || prepared is null)
            return;

        if (remaining <= context.CrossFadeDuration + TimeSpan.FromMilliseconds(500)
            || _retries.HasFailed(context.Source) && !CanPrepareRetry(context.Position.TotalSeconds, plan.AExit))
        {
            AutoMixLog.Write("mix could not be prepared in time, using crossfade");
            _planBlocked = true;
            _plan = null;
            return;
        }

        var exitRemaining = context.Duration.TotalSeconds - plan.AExit;
        if (remaining.TotalSeconds > exitRemaining + 20
            || Environment.TickCount64 < Interlocked.Read(ref _decodeRetryAfter))
            return;

        Volatile.Write(ref _decoding, 1);
        _taskRunner.Forget(
            DecodeAsync(context, plan, current, prepared, Volatile.Read(ref _epoch), _cts.Token),
            "prepare automix transition");
    }

    private void TryPlan(
        TrackTransitionContext context,
        SingleSongBase current,
        TransitionPreparedTrack prepared,
        TimeSpan remaining)
    {
        if (Math.Abs(context.PlaybackRate - 1) > 0.001)
            return;
        if ((_vocalsA is { IsCompleted: false } || _vocalsB is { IsCompleted: false }) && remaining.TotalSeconds > 40)
            return;
        if (_analysisA is not { IsCompletedSuccessfully: true } || _analysisA.Result is not { } a)
            return;
        if (_analysisB is not { IsCompletedSuccessfully: true } || _analysisB.Result is not { } b)
            return;

        var regionsA = _vocalsA is { IsCompletedSuccessfully: true } ? _vocalsA.Result : VocalRegions.Empty;
        var regionsB = _vocalsB is { IsCompletedSuccessfully: true } ? _vocalsB.Result : VocalRegions.Empty;
        var vocalsA = regionsA.Vocals;
        var vocalsB = regionsB.Vocals;
        var next = prepared.Song;
        var album = current.Album?.ActualId is { Length: > 0 } albumA && albumA != "0" && next.Album?.ActualId == albumA;
        AutoMixLog.Write($"analysis A {current.Name}: bpm {a.Bpm:0.0} conf {a.BeatConfidence:0.00} bar {a.DownbeatConfidence:0.00} beats {a.Beats.Length} lufs {a.IntegratedLoudness:0.0} sound {a.SoundStart:0.0}-{a.SoundEnd:0.0} outro {a.OutroStart:0.0} key {a.Key} vocals {vocalsA.Count} leads {regionsA.Leads.Count} by {a.BeatAnalyzer}/{a.KeyAnalyzer}/{_analysis.CurrentVocalAnalyzerId}");
        AutoMixLog.Write($"analysis B {next.Name}: bpm {b.Bpm:0.0} conf {b.BeatConfidence:0.00} bar {b.DownbeatConfidence:0.00} beats {b.Beats.Length} lufs {b.IntegratedLoudness:0.0} sound {b.SoundStart:0.0}-{b.SoundEnd:0.0} intro {b.IntroEnd:0.0} key {b.Key} vocals {vocalsB.Count} leads {regionsB.Leads.Count} by {b.BeatAnalyzer}/{b.KeyAnalyzer}/{_analysis.CurrentVocalAnalyzerId}");
        _planHadVocals = _vocalsA is { IsCompleted: true } && _vocalsB is { IsCompleted: true };
        _plan = AutoMixPlanner.Plan(a, b, vocalsA, vocalsB, new AutoMixPlannerOptions
        {
            MaxOverlapSeconds = Math.Clamp(_settings.MaxOverlap, 8, 60),
            CrossfadeSeconds = Math.Clamp(context.CrossFadeDuration.TotalSeconds, 2, 12),
            AllowTimeStretch = _settings.TimeStretch && TimeStretcher.IsAvailable,
            AlbumContinuation = album && _settings.KeepAlbumGapless
        }, regionsA.Leads, regionsB.Leads);
        AutoMixLog.Write($"plan {_plan.Technique}: exit A {_plan.AExit:0.00}s overlap {_plan.Overlap:0.00}s entry B {_plan.BEntry:0.00}s rate {_plan.Rate:0.0000} ramp {_plan.RampBack:0.0}s gainB {_plan.GainB:0.00} ({_plan.Reason}) stretch {TimeStretcher.IsAvailable}");
    }

    private async Task DecodeAsync(
        TrackTransitionContext context,
        MixPlan plan,
        SingleSongBase current,
        TransitionPreparedTrack prepared,
        int epoch,
        CancellationToken ct)
    {
        try
        {
            if (context.Source is not AudioGraphPlaybackSource
                || prepared.IncomingSource is not AudioGraphPlaybackSource incoming)
            {
                Block(epoch);
                return;
            }

            var rate = (int)Graph.SampleRate;
            if (rate <= 0)
            {
                Block(epoch);
                return;
            }

            var handoverStart = Math.Max(0, plan.AExit - 10);
            using var streamA = await _analysis.OpenLocalAudioAsync(current).ConfigureAwait(false);
            using var streamB = await _analysis.OpenLocalAudioAsync(prepared.Song).ConfigureAwait(false);
            if (streamA is null || streamB is null)
            {
                Interlocked.Exchange(ref _decodeRetryAfter, Environment.TickCount64 + 2000);
                return;
            }

            if (MediaSourceTraits.IsProgressive(incoming.PlaybackSource))
            {
                if (ReferenceEquals(_reloadedIncoming, prepared.Song))
                {
                    Block(epoch);
                    return;
                }

                _reloadedIncoming = prepared.Song;
                _reloadIncoming = true;
                return;
            }

            var pcmA = await AudioDecoder
                .DecodeAsync(streamA, (uint)rate, 2, TimeSpan.FromSeconds(handoverStart), TimeSpan.Zero, ct)
                .ConfigureAwait(false);
            var bLength = plan.BEntry + plan.Overlap * plan.Rate + plan.RampBack * Math.Max(1, plan.Rate) + 20;
            var bStart = Math.Max(0, plan.BEntry - 1);
            var pcmB = await AudioDecoder
                .DecodeAsync(streamB, (uint)rate, 2, TimeSpan.FromSeconds(bStart), TimeSpan.FromSeconds(bLength - bStart), ct)
                .ConfigureAwait(false);
            var regionA = new MixRegion { Samples = pcmA.Samples, SampleRate = pcmA.SampleRate, StartSeconds = handoverStart };
            var regionB = new MixRegion { Samples = pcmB.Samples, SampleRate = pcmB.SampleRate, StartSeconds = bStart };
            if (regionA.Frames < rate * 3 || regionB.Frames < rate * 3)
            {
                Block(epoch);
                return;
            }

            if (epoch == Volatile.Read(ref _epoch))
                Volatile.Write(ref _pending, new PendingMix(plan, regionA, regionB, prepared, epoch));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AutoMixLog.Write("prepare failed: " + ex.Message);
            Block(epoch);
        }
        finally
        {
            Volatile.Write(ref _decoding, 0);
        }
    }

    private void Block(int epoch)
    {
        if (epoch == Volatile.Read(ref _epoch))
            _planBlocked = true;
    }

    private bool TryInstall(TrackTransitionContext context, PendingMix pending)
    {
        if (pending.Epoch != Volatile.Read(ref _epoch)
            || !ReferenceEquals(_preloader.Prepared, pending.Prepared)
            || context.Source is not AudioGraphPlaybackSource outgoing
            || pending.Prepared.IncomingSource is not AudioGraphPlaybackSource incoming)
            return false;

        var graph = Graph;
        var plan = pending.Plan;
        AutoMixEngine? engine = null;
        MixRenderer? renderer = null;
        Timer? timer = null;
        try
        {
            var nodeVolumeA = graph.GetPlaybackSourceOutputVolume(outgoing);
            var nodeVolumeB = pending.Prepared.Ticket.TargetVolume;
            var gainA = nodeVolumeA * graph.GetPlaybackSourceAudioGain(outgoing);
            var gainB = nodeVolumeB * graph.GetPlaybackSourceAudioGain(incoming);
            engine = new AutoMixEngine(
                new GraphMixHost(graph),
                plan,
                pending.RegionA,
                pending.RegionB,
                outgoing,
                incoming,
                gainA,
                gainB,
                nodeVolumeA,
                nodeVolumeB);
            engine.TapA = graph.AttachTap(outgoing);
            graph.SetPlaybackSourceOutputVolume(0, incoming);
            graph.SeekPlaybackSource(TimeSpan.FromSeconds(plan.BEntry), incoming);
            renderer = graph.CreateMixRenderer(engine);
            var session = new MixSession
            {
                Host = context.Host,
                Engine = engine,
                Outgoing = outgoing,
                Incoming = incoming,
                Prepared = pending.Prepared,
                RegionA = pending.RegionA,
                RegionB = pending.RegionB,
                Renderer = renderer
            };
            session.GraphRebuilt = engine.RequestAbort;
            timer = new Timer(
                _ => _ = SuperviseAsync(session, null, CancellationToken.None),
                null,
                200,
                200);
            session.Timer = timer;
            _preloader.Take();
            graph.OnGraphRebuilt += session.GraphRebuilt;
            Volatile.Write(ref _session, session);
            var rate = (double)pending.RegionA.SampleRate;
            AutoMixLog.Write($"prepared: region A {pending.RegionA.StartSeconds:0.00}+{pending.RegionA.Frames / rate:0.0}s region B {pending.RegionB.StartSeconds:0.00}+{pending.RegionB.Frames / rate:0.0}s graph {rate:0}Hz");
            return true;
        }
        catch (Exception ex)
        {
            AutoMixLog.Write("prepare failed: " + ex.Message);
            timer?.Dispose();
            if (engine?.TapA is { } tapA)
                graph.DetachTap(outgoing, tapA);
            if (renderer is not null)
                graph.ReleaseMixRenderer(renderer);
            engine?.Dispose();
            try
            {
                graph.SeekPlaybackSource(TimeSpan.Zero, incoming);
            }
            catch
            {
            }

            _planBlocked = true;
            return false;
        }
    }

    private async Task SuperviseAsync(MixSession session, TrackTransitionContext? context, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref session.Supervising, 1, 0) != 0)
            return;

        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _session), session))
                return;

            var engine = session.Engine;
            if (engine.Stage == AutoMixStage.Waiting
                && double.IsNaN(engine.MapA)
                && !session.MapBusy
                && engine.TapA is { } tapA)
            {
                var position = OutgoingSeconds(session, context);
                if (position >= session.RegionA.StartSeconds + 0.4)
                {
                    session.MapBusy = true;
                    _taskRunner.Forget(MapOutgoingAsync(session, tapA, position), "align automix outgoing");
                }
            }

            if (engine.BStarted && !session.Transitioning && !engine.OutgoingFinished)
            {
                session.Transitioning = true;
                session.Host.SetTransitioning(true);
            }

            if (context is not null && engine.MidpointReached && session.Promotion is null && !session.CommitFailed)
                await CommitAsync(session, ct).ConfigureAwait(false);

            if (engine.OutgoingFinished && session.Promotion is not null && !session.OutgoingSettled)
            {
                await SettleOutgoingAsync(session).ConfigureAwait(false);
                if (session.Transitioning)
                {
                    session.Transitioning = false;
                    session.Host.SetTransitioning(false);
                }
            }

            if (engine.NeedsMapB && double.IsNaN(engine.MapB) && !session.MapBusy)
            {
                engine.TapB ??= Graph.AttachTap(session.Incoming);
                session.MapBusy = true;
                _taskRunner.Forget(MapIncomingAsync(session, engine.TapB), "align automix incoming");
            }

            TryForceHandBack(session);

            if (engine.Stage != session.LastStage)
            {
                AutoMixLog.Write($"stage {session.LastStage} -> {engine.Stage} progress {engine.Progress:0.00}");
                session.LastStage = engine.Stage;
            }

            if (engine.Stage is AutoMixStage.Done or AutoMixStage.Aborted)
                await FinishAsync(session).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AutoMixLog.Write("supervise failed: " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref session.Supervising, 0);
        }
    }

    private double OutgoingSeconds(MixSession session, TrackTransitionContext? context)
    {
        try
        {
            return Graph.GetAudioInputNode(session.Outgoing).Position.TotalSeconds;
        }
        catch
        {
            return context is not null && ReferenceEquals(context.Source, session.Outgoing)
                ? context.Position.TotalSeconds
                : double.NaN;
        }
    }

    private async Task MapOutgoingAsync(MixSession session, PlaybackTap tapA, double position)
    {
        try
        {
            var engine = session.Engine;
            var regionA = session.RegionA;
            var sampleRate = Graph.SampleRate;
            var expected = (long)Math.Round((position - regionA.StartSeconds) * sampleRate);
            await Task.Delay(400).ConfigureAwait(false);
            var map = await Task
                .Run(() => TapAligner.FindMap(tapA, regionA, expected + (long)(sampleRate * 0.4)))
                .ConfigureAwait(false);
            if (map is not null)
            {
                engine.MapA = map.Value;
                var actual = regionA.StartSeconds + (tapA.TotalFrames + map.Value) / sampleRate;
                AutoMixLog.Write($"map A: {map.Value:0} after {session.MapAFailures} misses, position {position:0.00}s plays content {actual:0.00}s");
            }
            else if (session.MapAFailures++ == 0)
            {
                AutoMixLog.Write("map A: not found, retrying");
            }
        }
        finally
        {
            session.MapBusy = false;
        }
    }

    private async Task MapIncomingAsync(MixSession session, PlaybackTap tapB)
    {
        try
        {
            var engine = session.Engine;
            await Task.Delay(600).ConfigureAwait(false);
            var expected = (long)Math.Round(engine.BContentIndex);
            var regionB = session.RegionB;
            var map = await Task.Run(() => TapAligner.FindMap(tapB, regionB, expected)).ConfigureAwait(false);
            if (map is not null)
                engine.MapB = map.Value;
            else
                session.MapBFailures++;
            AutoMixLog.Write($"map B: {(map is null ? "not found" : map.Value.ToString("0"))}");
        }
        finally
        {
            session.MapBusy = false;
        }
    }

    private void TryForceHandBack(MixSession session)
    {
        var engine = session.Engine;
        if (engine.Stage is AutoMixStage.Done or AutoMixStage.Aborted)
            return;

        var limit = engine.Plan.Overlap + engine.Plan.RampBack + 16;
        var elapsed = engine.SecondsSinceBStarted;
        if (elapsed > limit + 8)
        {
            if (!session.WatchdogFired)
                AutoMixLog.Write($"watchdog abort at stage {engine.Stage} after {elapsed:0.0}s");
            session.WatchdogFired = true;
            engine.RequestAbort();
            return;
        }

        if (session.ForcedHandBack || engine.Stage is not (AutoMixStage.RampBack or AutoMixStage.Aligning))
            return;

        var reason = engine.NeedsForcedHandBack ? "region exhausted"
            : engine.NeedsMapB && session.MapBFailures >= 3 ? "map B failed"
            : elapsed > limit ? "watchdog" : null;
        if (reason is null)
            return;

        var position = engine.BContentSeconds;
        if (double.IsNaN(position))
            return;

        session.ForcedHandBack = true;
        AutoMixLog.Write($"forced hand-back ({reason}) at B {position:0.00}s");
        try
        {
            _player.SetPlaybackSourceSpeed(1, session.Incoming);
            _player.SeekPlaybackSource(TimeSpan.FromSeconds(position + 0.03), session.Incoming);
            _ = session.Host.RampVolumeAsync(session.Incoming, engine.NodeVolumeB, _forcedHandBackTime);
        }
        catch
        {
        }

        engine.BeginForcedHandBack(_forcedHandBackTime.TotalSeconds);
    }

    private async Task CommitAsync(MixSession session, CancellationToken ct)
    {
        PreparedPlaybackPromotion? promotion;
        try
        {
            promotion = await session.Host.PromoteAsync(session.Prepared, ct).ConfigureAwait(false);
        }
        catch
        {
            promotion = null;
        }

        if (promotion is null)
        {
            session.CommitFailed = true;
            AutoMixLog.Write("commit rejected, aborting transition");
            session.Engine.RequestAbort();
            return;
        }

        session.Promotion = promotion;
    }

    private async Task SettleOutgoingAsync(MixSession session)
    {
        if (session.Engine.TapA is { } tapA)
        {
            Graph.DetachTap(session.Outgoing, tapA);
            session.Engine.TapA = null;
        }

        session.OutgoingSettled = true;
        if (session.Promotion is { } promotion)
            await promotion.SettleOutgoingAsync().ConfigureAwait(false);
    }

    private async Task StartPreparedAsync(
        TrackTransitionContext context,
        TransitionPreparedTrack prepared,
        CancellationToken ct)
    {
        PreparedPlaybackPromotion? promotion;
        try
        {
            await prepared.Ticket.PlayAsync(ct).ConfigureAwait(false);
            promotion = await context.Host.PromoteAsync(prepared, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch
        {
            promotion = null;
        }

        if (promotion is null)
        {
            await prepared.Ticket.DisposeAsync().ConfigureAwait(false);
            await context.Host.AdvanceDirectAsync(context, ct).ConfigureAwait(false);
            return;
        }

        if (prepared.IncomingSource is { } incoming)
            _ = context.Host.RampVolumeAsync(incoming, prepared.Ticket.TargetVolume, _catchUpTime);
        else
            await promotion.Incoming.SetVolumeAsync(promotion.Incoming.TargetVolume, CancellationToken.None).ConfigureAwait(false);
        await promotion.SettleOutgoingAsync().ConfigureAwait(false);
    }

    private async Task AbortAsync(MixSession session)
    {
        var engine = session.Engine;
        session.Cancelled = true;
        engine.RequestAbort();
        for (var i = 0; i < 20 && engine.Stage is not (AutoMixStage.Aborted or AutoMixStage.Done); i++)
            await Task.Delay(15).ConfigureAwait(false);
        await FinishAsync(session).ConfigureAwait(false);
    }

    private async Task FinishAsync(MixSession session)
    {
        if (Interlocked.CompareExchange(ref _session, null, session) != session)
            return;

        var graph = Graph;
        var engine = session.Engine;
        AutoMixLog.WriteSummary(engine, session.Renderer.Stats);
        session.Timer?.Dispose();
        if (session.GraphRebuilt is { } rebuilt)
            graph.OnGraphRebuilt -= rebuilt;
        if (engine.TapA is { } tapA)
        {
            graph.DetachTap(session.Outgoing, tapA);
            engine.TapA = null;
        }

        if (engine.TapB is { } tapB)
        {
            graph.DetachTap(session.Incoming, tapB);
            engine.TapB = null;
        }

        var renderer = session.Renderer;
        _ = Task.Run(async () =>
        {
            await Task.Delay(100).ConfigureAwait(false);
            graph.ReleaseMixRenderer(renderer);
            engine.Dispose();
        });

        if (session.Transitioning)
        {
            session.Transitioning = false;
            session.Host.SetTransitioning(false);
        }

        if (session.Promotion is { } promotion)
        {
            try
            {
                graph.SetPlaybackSourceSpeed(1, session.Incoming);
            }
            catch
            {
            }

            try
            {
                await promotion.Incoming
                    .SetVolumeAsync(promotion.Incoming.TargetVolume, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            if (!session.OutgoingSettled)
            {
                session.OutgoingSettled = true;
                try
                {
                    await promotion.SettleOutgoingAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }

            return;
        }

        if (session.Cancelled)
        {
            _blockedFor = session.Outgoing;
            _planBlocked = true;
        }
        else
        {
            var failures = _retries.RegisterFailure(session.Outgoing);
            if (failures <= MixRetryBudget.MaxRetries)
            {
                AutoMixLog.Write($"transition aborted, retrying mix ({failures}/{MixRetryBudget.MaxRetries})");
                Interlocked.Increment(ref _epoch);
                Volatile.Write(ref _pending, null);
                _replan = true;
            }
            else
            {
                AutoMixLog.Write($"transition aborted after {MixRetryBudget.MaxRetries} retries, using crossfade for this track");
                _blockedFor = session.Outgoing;
                _planBlocked = true;
            }
        }

        _ = session.Host.RampVolumeAsync(session.Outgoing, engine.NodeVolumeA, _restoreTime);
        if (session.KeepPrepared)
        {
            try
            {
                graph.SetPlaybackSourceSpeed(1, session.Incoming);
            }
            catch
            {
            }

            return;
        }

        try
        {
            await session.Prepared.Ticket.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void ResetForTrack()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        Interlocked.Increment(ref _epoch);
        Volatile.Write(ref _pending, null);
        _analysisA = null;
        _analysisB = null;
        _vocalsA = null;
        _vocalsB = null;
        _plan = null;
        _planBlocked = false;
        _blockedFor = null;
        _reloadIncoming = false;
        _replan = false;
        _retries.Reset();
        _plannedFor = null;
        _analyzedFor = null;
        _aheadFor = null;
        _aheadTask = null;
        Interlocked.Exchange(ref _decodeRetryAfter, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Volatile.Read(ref _session)?.Timer?.Dispose();
        _cts.Cancel();
    }

    internal static bool CanPrepareRetry(double positionSeconds, double exitSeconds)
    {
        return positionSeconds <= exitSeconds - RetryLeadSeconds;
    }

    internal sealed class MixRetryBudget
    {
        internal const int MaxRetries = 2;
        private readonly object _sync = new();
        private object? _track;
        private int _failures;

        public int RegisterFailure(object track)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_track, track))
                {
                    _track = track;
                    _failures = 0;
                }

                return ++_failures;
            }
        }

        public bool HasFailed(object track)
        {
            lock (_sync)
            {
                return _failures > 0 && ReferenceEquals(_track, track);
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _track = null;
                _failures = 0;
            }
        }
    }

    private sealed record PendingMix(
        MixPlan Plan,
        MixRegion RegionA,
        MixRegion RegionB,
        TransitionPreparedTrack Prepared,
        int Epoch);

    private sealed class MixSession
    {
        public required ITrackTransitionHost Host { get; init; }
        public required AutoMixEngine Engine { get; init; }
        public required AudioGraphPlaybackSource Outgoing { get; init; }
        public required AudioGraphPlaybackSource Incoming { get; init; }
        public required TransitionPreparedTrack Prepared { get; init; }
        public required MixRegion RegionA { get; init; }
        public required MixRegion RegionB { get; init; }
        public required MixRenderer Renderer { get; init; }
        public Action? GraphRebuilt;
        public Timer? Timer;
        public PreparedPlaybackPromotion? Promotion;
        public int Supervising;
        public volatile bool MapBusy;
        public volatile bool KeepPrepared;
        public volatile bool Cancelled;
        public bool Transitioning;
        public bool CommitFailed;
        public bool OutgoingSettled;
        public bool ForcedHandBack;
        public bool WatchdogFired;
        public int MapAFailures;
        public int MapBFailures;
        public AutoMixStage LastStage = AutoMixStage.Waiting;
    }
}
