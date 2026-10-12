using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using HyPlayer.Classes;
using HyPlayer.Features.Playback.Analysis.Dsp;
using HyPlayer.Features.Playback.Models;
using HyPlayer.Platform.Playback.MediaSources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Features.Playback.Analysis;

public sealed class BeatAnalysisResult
{
    public required string Analyzer { get; init; }
    public double Bpm { get; init; }
    public double Confidence { get; init; }
    public double[] Beats { get; init; } = Array.Empty<double>();
    public double[] Downbeats { get; init; } = Array.Empty<double>();
    public double DownbeatConfidence { get; init; } = 1;
}

public interface IBeatAnalyzer
{
    string Id { get; }
    int Priority { get; }
    bool IsAvailable { get; }
    Task<BeatAnalysisResult?> AnalyzeAsync(float[] mono, int sampleRate, CancellationToken ct);
}

public interface IKeyAnalyzer
{
    string Id { get; }
    int Priority { get; }
    bool IsAvailable { get; }
    Task<(int key, double confidence)?> AnalyzeAsync(float[] mono, int sampleRate, CancellationToken ct);
}

public interface IVocalAnalyzer
{
    string Id { get; }
    bool IsAvailable { get; }
    Task<VocalActivity?> AnalyzeAsync(float[] stereo, int sampleRate, double offsetSeconds, CancellationToken ct);
}

public interface IAudioAnalysisService
{
    Task<VocalActivity?> AnalyzeVocalsAsync(SingleSongBase item, bool outro, CancellationToken ct);
    string CurrentVocalAnalyzerId { get; }
    TrackAnalysis? TryGetCached(SingleSongBase item);
    Task<TrackAnalysis?> AnalyzeAsync(SingleSongBase item, CancellationToken ct);
    Task<IRandomAccessStream?> OpenLocalAudioAsync(SingleSongBase item);
    string CurrentBeatAnalyzerId { get; }
    string CurrentKeyAnalyzerId { get; }
}

public sealed class AudioAnalysisService : IAudioAnalysisService
{
    public const int AnalysisSampleRate = 22050;

    private readonly IMediaSourceService _mediaSources;
    private readonly IEnumerable<IBeatAnalyzer> _beatAnalyzers;
    private readonly IEnumerable<IKeyAnalyzer> _keyAnalyzers;
    private readonly ModelManager _models;
    private readonly IEnumerable<IVocalAnalyzer> _vocalAnalyzers;
    private readonly ConcurrentDictionary<string, Task<VocalActivity?>> _vocalInflight = new();
    private readonly ConcurrentDictionary<string, VocalActivity> _leadMemory = new();
    private const double VocalWindowSeconds = 32;
    private readonly ConcurrentDictionary<string, TrackAnalysis> _memory = new();
    private readonly ConcurrentDictionary<string, Task<TrackAnalysis?>> _inflight = new();
    private readonly SemaphoreSlim _worker = new(1, 1);

    public AudioAnalysisService(IMediaSourceService mediaSources, IEnumerable<IBeatAnalyzer> beatAnalyzers, IEnumerable<IKeyAnalyzer> keyAnalyzers, IEnumerable<IVocalAnalyzer> vocalAnalyzers, ModelManager models)
    {
        _mediaSources = mediaSources;
        _models = models;
        _vocalAnalyzers = vocalAnalyzers;
        _beatAnalyzers = beatAnalyzers;
        _keyAnalyzers = keyAnalyzers;
    }

    public string CurrentBeatAnalyzerId => BeatAnalyzer()?.Id ?? TrackAnalysis.DspAnalyzer;
    public string CurrentKeyAnalyzerId => KeyAnalyzer()?.Id ?? TrackAnalysis.DspAnalyzer;
    public string CurrentVocalAnalyzerId => VocalAnalyzer()?.Id ?? MidBandActivity.Id;

    private IVocalAnalyzer? VocalAnalyzer() => _vocalAnalyzers.FirstOrDefault(a => a.IsAvailable);

    public Task<VocalActivity?> AnalyzeVocalsAsync(SingleSongBase item, bool outro, CancellationToken ct)
    {
        var analyzer = VocalAnalyzer();
        var key = $"{KeyOf(item)}_{(outro ? "out" : "in")}";
        return _vocalInflight.GetOrAdd(key, _ => RunVocalsAsync(item, analyzer, outro, key, ct));
    }

    private async Task<VocalActivity?> RunVocalsAsync(SingleSongBase item, IVocalAnalyzer? analyzer, bool outro, string inflightKey, CancellationToken ct)
    {
        try
        {
            var analysis = await AnalyzeAsync(item, ct);
            if (analysis is null) return null;
            var id = analyzer?.Id ?? MidBandActivity.Id;
            var cached = analysis.VocalAnalyzer == id ? outro ? analysis.VocalOutroCurve : analysis.VocalIntroCurve : null;
            var cachedWindow = outro ? analysis.VocalOutroWindow : analysis.VocalIntroWindow;
            if (cached is not null && cachedWindow is { Length: 2 }) return new VocalActivity(cachedWindow[0], cached) { Analyzer = id };
            if (analyzer is null && _leadMemory.TryGetValue(inflightKey, out var remembered)) return remembered;

            var (from, length) = VocalWindow(analysis.SoundStart, analysis.SoundEnd, analysis.Duration, outro);
            if (length < 5) return null;
            using var stream = await OpenLocalAudioAsync(item);
            if (stream is null) return null;
            await _worker.WaitAsync(ct);
            try
            {
                var activity = await MeasureVocalsAsync(stream, analyzer, from, length, analysis.IntegratedLoudness, ct);
                if (activity is null) return null;
                if (analyzer is null && HasModelVocals(analysis))
                {
                    _leadMemory[inflightKey] = activity;
                    return activity;
                }
                StoreVocals(analysis, id, outro, activity);
                await SaveAsync(KeyOf(item), analysis);
                return activity;
            }
            finally
            {
                _worker.Release();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            _vocalInflight.TryRemove(inflightKey, out _);
        }
    }

    private static async Task<VocalActivity?> MeasureVocalsAsync(IRandomAccessStream stream, IVocalAnalyzer? analyzer, double from, double length, double loudness, CancellationToken ct)
    {
        if (analyzer is null)
        {
            var mono = await AudioDecoder.DecodeAsync(stream, AnalysisSampleRate, 1, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(length), ct);
            return await AnalysisWorker.Run(() => MidBandActivity.Analyze(mono.Samples, mono.SampleRate, 0, mono.Samples.Length, from, loudness), ct);
        }
        var stereo = await AudioDecoder.DecodeAsync(stream, 44100, 2, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(length), ct);
        return await analyzer.AnalyzeAsync(stereo.Samples, stereo.SampleRate, from, ct);
    }

    private static (double from, double length) VocalWindow(double soundStart, double soundEnd, double duration, bool outro)
    {
        var from = outro ? Math.Max(0, soundEnd - VocalWindowSeconds) : Math.Max(0, soundStart);
        return (from, Math.Min(VocalWindowSeconds, Math.Max(0, duration - from)));
    }

    private static bool HasModelVocals(TrackAnalysis analysis) =>
        analysis.VocalAnalyzer.Length > 0 && analysis.VocalAnalyzer != MidBandActivity.Id;

    private static void StoreVocals(TrackAnalysis analysis, string analyzer, bool outro, VocalActivity activity)
    {
        if (analysis.VocalAnalyzer != analyzer)
        {
            analysis.VocalAnalyzer = analyzer;
            analysis.VocalIntroCurve = analysis.VocalOutroCurve = null;
            analysis.VocalIntroWindow = analysis.VocalOutroWindow = null;
        }
        if (outro)
        {
            analysis.VocalOutroCurve = activity.Values;
            analysis.VocalOutroWindow = [activity.Start, activity.End];
        }
        else
        {
            analysis.VocalIntroCurve = activity.Values;
            analysis.VocalIntroWindow = [activity.Start, activity.End];
        }
    }

    private IBeatAnalyzer? BeatAnalyzer() => _beatAnalyzers.Where(a => a.IsAvailable).OrderByDescending(a => a.Priority).FirstOrDefault();
    private IKeyAnalyzer? KeyAnalyzer() => _keyAnalyzers.Where(a => a.IsAvailable).OrderByDescending(a => a.Priority).FirstOrDefault();

    internal static string KeyOf(SingleSongBase item) => $"{item.ProviderId}_{NeteaseTrack.IdOf(item)}";

    private bool IsCurrent(TrackAnalysis analysis) =>
        analysis.IsCurrentVersion &&
        analysis.BeatAnalyzer == CurrentBeatAnalyzerId &&
        analysis.KeyAnalyzer == CurrentKeyAnalyzerId;

    public TrackAnalysis? TryGetCached(SingleSongBase item)
    {
        return _memory.TryGetValue(KeyOf(item), out var analysis) && IsCurrent(analysis) ? analysis : null;
    }

    public Task<TrackAnalysis?> AnalyzeAsync(SingleSongBase item, CancellationToken ct)
    {
        var key = KeyOf(item);
        if (_memory.TryGetValue(key, out var cached) && IsCurrent(cached)) return Task.FromResult<TrackAnalysis?>(cached);
        return _inflight.GetOrAdd(key, _ => RunAsync(item, key, ct));
    }

    private async Task<TrackAnalysis?> RunAsync(SingleSongBase item, string key, CancellationToken ct)
    {
        try
        {
            await _models.EnsureRefreshedAsync();
            var stored = await LoadStoredAsync(key);
            if (stored is not null && IsCurrent(stored))
            {
                _memory[key] = stored;
                return stored;
            }

            using var stream = await OpenLocalAudioAsync(item);
            if (stream is null) return null;

            await _worker.WaitAsync(ct);
            try
            {
                var analysis = await Task.Run(() => AnalyzeStreamAsync(stream, key, stored, ct), ct);
                if (analysis is null) return null;
                _memory[key] = analysis;
                await SaveAsync(key, analysis);
                return analysis;
            }
            finally
            {
                _worker.Release();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private async Task<TrackAnalysis?> AnalyzeStreamAsync(IRandomAccessStream stream, string key, TrackAnalysis? previous, CancellationToken ct)
    {
        if (previous is { Version: < TrackAnalysis.ModelBeatVersion }) previous = null;
        var pcm = await AudioDecoder.DecodeAsync(stream, AnalysisSampleRate, 1, TimeSpan.Zero, TimeSpan.Zero, ct);
        var mono = pcm.Samples;
        var sr = pcm.SampleRate;
        if (mono.Length < sr * 5) return null;
        ct.ThrowIfCancellationRequested();

        var (features, soundStart, soundEnd, loudness) = await AnalysisWorker.Run(() =>
        {
            var spectral = SpectralFeatures.Compute(mono, sr);
            var bounds = LevelAnalysis.SoundBounds(mono, sr);
            return (spectral, bounds.start, bounds.end, LevelAnalysis.IntegratedLoudness(mono, sr));
        }, ct);

        BeatAnalysisResult? beats = null;
        var beatAnalyzer = BeatAnalyzer();
        var beatAnalyzerId = beatAnalyzer?.Id ?? TrackAnalysis.DspAnalyzer;
        if (previous is { HasCurrentBeats: true, HasBeatGrid: true } && previous.BeatAnalyzer == beatAnalyzerId)
        {
            beats = new BeatAnalysisResult
            {
                Analyzer = previous.BeatAnalyzer,
                Bpm = previous.Bpm,
                Confidence = previous.BeatConfidence,
                Beats = previous.Beats,
                Downbeats = previous.Downbeats,
                DownbeatConfidence = previous.DownbeatConfidence
            };
        }
        else if (beatAnalyzer is not null)
        {
            try
            {
                beats = await beatAnalyzer.AnalyzeAsync(mono, sr, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                beats = null;
            }
        }
        if (beats is null)
        {
            var dsp = await AnalysisWorker.Run(() => BeatTracker.Track(features), ct);
            beats = new BeatAnalysisResult
            {
                Analyzer = TrackAnalysis.DspAnalyzer,
                Bpm = dsp.Bpm,
                Confidence = dsp.Confidence,
                Beats = dsp.Beats,
                Downbeats = dsp.Downbeats,
                DownbeatConfidence = dsp.DownbeatConfidence
            };
        }
        ct.ThrowIfCancellationRequested();

        (int key, double confidence)? keyResult = null;
        var keyAnalyzer = KeyAnalyzer();
        if (previous is not null && previous.KeyAnalyzer == (keyAnalyzer?.Id ?? TrackAnalysis.DspAnalyzer))
        {
            keyResult = (previous.Key, previous.KeyConfidence);
        }
        else if (keyAnalyzer is not null)
        {
            try
            {
                keyResult = await keyAnalyzer.AnalyzeAsync(mono, sr, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                keyResult = null;
            }
        }
        keyResult ??= await AnalysisWorker.Run(() => KeyEstimator.Estimate(mono, sr), ct);

        var barEnergies = LevelAnalysis.BarEnergies(features, beats.Downbeats);
        var (introEnd, outroStart) = LevelAnalysis.IntroOutro(barEnergies, beats.Downbeats);
        var (tempoRuns, sections) = await AnalysisWorker.Run(() => Structure(mono, sr, features, beats, soundStart, soundEnd), ct);
        var vocals = previous is not null && HasModelVocals(previous) ? previous : null;
        var analysis = new TrackAnalysis
        {
            TrackKey = key,
            BeatAnalyzer = beatAnalyzerId,
            KeyAnalyzer = keyAnalyzer?.Id ?? TrackAnalysis.DspAnalyzer,
            Duration = Math.Round(pcm.Seconds, 3),
            SoundStart = Math.Round(soundStart, 3),
            SoundEnd = Math.Round(soundEnd, 3),
            IntegratedLoudness = loudness,
            Bpm = Math.Round(beats.Bpm, 3),
            BeatConfidence = beats.Confidence,
            Beats = beats.Beats.Select(b => Math.Round(b, 4)).ToArray(),
            Downbeats = beats.Downbeats.Select(b => Math.Round(b, 4)).ToArray(),
            DownbeatConfidence = beats.DownbeatConfidence,
            BarEnergies = barEnergies,
            IntroEnd = Math.Round(introEnd, 3),
            OutroStart = Math.Round(outroStart, 3),
            Key = keyResult.Value.key,
            KeyConfidence = keyResult.Value.confidence,
            Envelope = LevelAnalysis.Envelope(features.Rms, features.HopSeconds, 10),
            EnvelopeRate = 10,
            VocalAnalyzer = vocals?.VocalAnalyzer ?? string.Empty,
            VocalIntroWindow = vocals?.VocalIntroWindow,
            VocalIntroCurve = vocals?.VocalIntroCurve,
            VocalOutroWindow = vocals?.VocalOutroWindow,
            VocalOutroCurve = vocals?.VocalOutroCurve,
            TempoRuns = tempoRuns,
            Sections = sections
        };
        if (VocalAnalyzer() is not null) return analysis;
        foreach (var outro in new[] { false, true })
        {
            var (from, length) = VocalWindow(analysis.SoundStart, analysis.SoundEnd, analysis.Duration, outro);
            if (length < 5) continue;
            var activity = await AnalysisWorker.Run(() => MidBandActivity.Analyze(mono, sr, (int)(from * sr), (int)(length * sr), from, loudness), ct);
            if (activity is null) continue;
            if (vocals is null) StoreVocals(analysis, MidBandActivity.Id, outro, activity);
            else _leadMemory[$"{key}_{(outro ? "out" : "in")}"] = activity;
        }
        return analysis;
    }

    internal static (TempoRun[] Runs, TrackSection[] Sections) Structure(float[] mono, int sampleRate, SpectralFeatures features,
        BeatAnalysisResult beats, double soundStart, double soundEnd)
    {
        var runs = beats.Analyzer == TrackAnalysis.DspAnalyzer ? TempoRuns.Find(beats.Beats, features) : TempoRuns.Find(beats.Beats);
        var sections = SectionAnalysis.Analyze(mono, sampleRate, soundStart, soundEnd, beats.Beats, beats.Downbeats,
            beats.DownbeatConfidence >= SectionAnalysis.TrustedBarConfidence);
        return (runs, sections);
    }

    public async Task<IRandomAccessStream?> OpenLocalAudioAsync(SingleSongBase item)
    {
        try
        {
            return await _mediaSources.OpenLocalAudioAsync(item, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<StorageFolder> AnalysisFolderAsync() =>
        await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Analysis", CreationCollisionOption.OpenIfExists);

    private static string FileNameOf(string key)
    {
        if (key.Length > 64)
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".json";
        var chars = key.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray();
        return new string(chars) + ".json";
    }

    private static async Task<TrackAnalysis?> LoadStoredAsync(string key)
    {
        try
        {
            var folder = await AnalysisFolderAsync();
            if (await folder.TryGetItemAsync(FileNameOf(key)) is not StorageFile file) return null;
            var json = await FileIO.ReadTextAsync(file);
            return JsonSerializer.Deserialize(json, JsonDefaultContext.Default.TrackAnalysis);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveAsync(string key, TrackAnalysis analysis)
    {
        try
        {
            var folder = await AnalysisFolderAsync();
            var file = await folder.CreateFileAsync(FileNameOf(key), CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(analysis, JsonDefaultContext.Default.TrackAnalysis));
        }
        catch
        {
        }
    }
}
