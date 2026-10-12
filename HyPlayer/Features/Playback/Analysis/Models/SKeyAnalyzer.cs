using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Features.Playback.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HyPlayer.Features.Playback.Analysis.Models;

public sealed class SKeyAnalyzer : IKeyAnalyzer
{
    private const int SampleRate = 22050;
    private const int MaxSeconds = 240;

    private readonly ModelManager _models;

    public SKeyAnalyzer(ModelManager models)
    {
        _models = models;
    }

    public string Id => "skey";
    public int Priority => 10;
    public bool IsAvailable => _models.IsUsable(ModelCatalog.SKeyId);

    public async Task<(int key, double confidence)?> AnalyzeAsync(float[] mono, int sampleRate, CancellationToken ct)
    {
        if (sampleRate != SampleRate || mono.Length < SampleRate * 3) return null;
        var length = Math.Min(mono.Length, SampleRate * MaxSeconds);
        var start = (mono.Length - length) / 2;
        using var lease = await _models.AcquireSessionAsync(ModelCatalog.SKeyId);
        var scores = await AnalysisWorker.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var input = new DenseTensor<float>(mono.AsMemory(start, length), [1, length]);
            using var results = lease.Session.Run([NamedOnnxValue.CreateFromTensor("audio", input)]);
            return results.First(r => r.Name == "scores").AsTensor<float>().ToArray();
        }, ct);
        if (scores.Length != 24) return null;
        var order = Enumerable.Range(0, 24).OrderByDescending(i => scores[i]).ToArray();
        var best = order[0];
        if (!float.IsFinite(scores[best])) return null;
        return (ToKey(best), Math.Round(Math.Max(0, scores[best] - scores[order[1]]), 3));
    }

    private static int ToKey(int label) => label < 12 ? (9 + label) % 12 : 12 + (11 + label - 12) % 12;
}
