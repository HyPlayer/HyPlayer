using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Features.Playback.Analysis.Dsp;
using HyPlayer.Features.Playback.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HyPlayer.Features.Playback.Analysis.Models;

public sealed class BeatThisAnalyzer : IBeatAnalyzer
{
    private const int SampleRate = 22050;
    private const int NFft = 1024;
    private const int Hop = 441;
    private const int Bins = 128;
    private const double Fps = 50;
    private const int Chunk = 1500;
    private const int Border = 6;

    private readonly ModelManager _models;
    private float[]? _filterbank;
    private int[]? _bandStart;
    private int[]? _bandEnd;

    public BeatThisAnalyzer(ModelManager models)
    {
        _models = models;
    }

    public string Id => "beatthis";
    public int Priority => 10;
    public bool IsAvailable => _models.IsUsable(ModelCatalog.BeatThisId);

    public async Task<BeatAnalysisResult?> AnalyzeAsync(float[] mono, int sampleRate, CancellationToken ct)
    {
        if (sampleRate != SampleRate || mono.Length < SampleRate * 5) return null;
        await EnsureFilterbankAsync();
        var spect = await AnalysisWorker.Run(() => MelSpectrogram(mono, ct), ct);
        var frames = spect.Length / Bins;
        float[] beatLogits, downLogits;
        using (var lease = await _models.AcquireSessionAsync(ModelCatalog.BeatThisId))
            (beatLogits, downLogits) = await AnalysisWorker.Run(() => Infer(lease.Session, spect, frames, ct), ct);

        var peaks = PickPeaks(beatLogits);
        if (peaks.Count < 8) return null;
        var period = BasePeriod(peaks);
        var beats = CleanGrid(peaks, beatLogits, period);
        var downbeats = PickPeaks(downLogits)
            .Select(t => beats[NearestIndex(beats, t)])
            .Distinct()
            .ToArray();
        var ibis = beats.Zip(beats.Skip(1), (a, b) => b - a).OrderBy(x => x).ToList();
        var median = ibis[ibis.Count / 2];
        var steady = SteadyPeriod(beats, period);
        return new BeatAnalysisResult
        {
            Analyzer = Id,
            Bpm = steady > 0 ? 60 / steady : 60 / median,
            Confidence = Confidence(beatLogits, beats, ibis, median),
            Beats = beats.ToArray(),
            Downbeats = downbeats
        };
    }

    private static double BasePeriod(List<double> beats)
    {
        var ibis = beats.Zip(beats.Skip(1), (a, b) => b - a).Where(x => x is > 0.2 and < 2.0).ToList();
        if (ibis.Count == 0) return 0.5;
        var bins = new Dictionary<int, int>();
        foreach (var ibi in ibis)
        {
            var bin = (int)Math.Round(ibi / 0.02);
            bins[bin] = bins.GetValueOrDefault(bin) + 1;
        }
        var modeBin = bins.Keys.MaxBy(k => bins.GetValueOrDefault(k - 1) + bins[k] + bins.GetValueOrDefault(k + 1));
        var mode = modeBin * 0.02;
        double Share(double period) => ibis.Count(x => Math.Abs(x - period) <= period * 0.08) / (double)ibis.Count;
        double Center(double period)
        {
            var near = ibis.Where(x => Math.Abs(x - period) <= period * 0.08).OrderBy(x => x).ToList();
            return near.Count == 0 ? period : near[near.Count / 2];
        }
        foreach (var candidate in new[] { mode / 2, mode, mode * 2 })
        {
            if (candidate < 0.25 || candidate > 1.5) continue;
            if (Share(candidate) >= 0.2) return Center(candidate);
        }
        return Center(mode);
    }

    private static List<double> CleanGrid(List<double> peaks, float[] logits, double period)
    {
        float Strength(double t) => logits[Math.Clamp((int)Math.Round(t * Fps), 0, logits.Length - 1)];
        var kept = new List<double>(peaks.Count) { peaks[0] };
        for (var i = 1; i < peaks.Count; i++)
        {
            if (peaks[i] - kept[^1] < period * 0.6)
            {
                if (Strength(peaks[i]) > Strength(kept[^1])) kept[^1] = peaks[i];
                continue;
            }
            kept.Add(peaks[i]);
        }
        var filled = new List<double>(kept.Count * 2) { kept[0] };
        for (var i = 1; i < kept.Count; i++)
        {
            var gap = kept[i] - kept[i - 1];
            var multiple = (int)Math.Round(gap / period);
            if (multiple is >= 2 and <= 4 && Math.Abs(gap - multiple * period) <= period * 0.12)
                for (var k = 1; k < multiple; k++) filled.Add(kept[i - 1] + gap * k / multiple);
            filled.Add(kept[i]);
        }
        return filled;
    }

    private static double SteadyPeriod(List<double> beats, double period)
    {
        var bestStart = 0;
        var bestLength = 0;
        var start = 0;
        for (var i = 1; i <= beats.Count; i++)
        {
            var steady = i < beats.Count && Math.Abs(beats[i] - beats[i - 1] - period) <= period * 0.1;
            if (steady) continue;
            if (i - start > bestLength)
            {
                bestLength = i - start;
                bestStart = start;
            }
            start = i;
        }
        if (bestLength < 16) return 0;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < bestLength; i++)
        {
            var y = beats[bestStart + i];
            sumX += i;
            sumY += y;
            sumXY += i * y;
            sumXX += (double)i * i;
        }
        var slope = (bestLength * sumXY - sumX * sumY) / (bestLength * sumXX - sumX * sumX);
        return slope > 0 ? slope : 0;
    }

    private async Task EnsureFilterbankAsync()
    {
        if (_filterbank is not null) return;
        var bytes = await _models.ReadFileAsync(ModelCatalog.BeatThisId, "mel-filterbank.bin");
        var fb = new float[bytes.Length / 4];
        System.Buffer.BlockCopy(bytes, 0, fb, 0, fb.Length * 4);
        if (fb.Length != (NFft / 2 + 1) * Bins) throw new InvalidOperationException("mel filterbank shape");
        var start = new int[Bins];
        var end = new int[Bins];
        for (var m = 0; m < Bins; m++)
        {
            start[m] = -1;
            for (var k = 0; k <= NFft / 2; k++)
            {
                if (fb[k * Bins + m] == 0) continue;
                if (start[m] < 0) start[m] = k;
                end[m] = k + 1;
            }
            if (start[m] < 0) start[m] = end[m] = 0;
        }
        _bandStart = start;
        _bandEnd = end;
        _filterbank = fb;
    }

    private float[] MelSpectrogram(float[] mono, CancellationToken ct)
    {
        var fb = _filterbank!;
        var count = mono.Length;
        var frames = count / Hop + 1;
        var window = new float[NFft];
        for (var i = 0; i < NFft; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft));
        var fft = new Fft(NFft);
        var re = new float[NFft];
        var im = new float[NFft];
        var magnitude = new float[NFft / 2 + 1];
        var norm = (float)(1 / Math.Sqrt(NFft));
        var output = new float[frames * Bins];
        for (var f = 0; f < frames; f++)
        {
            if ((f & 1023) == 0) ct.ThrowIfCancellationRequested();
            var center = f * Hop - NFft / 2;
            for (var i = 0; i < NFft; i++)
            {
                var index = center + i;
                if (index < 0) index = -index;
                if (index >= count) index = 2 * (count - 1) - index;
                index = Math.Clamp(index, 0, count - 1);
                re[i] = mono[index] * window[i];
                im[i] = 0;
            }
            fft.Forward(re, im);
            for (var k = 0; k <= NFft / 2; k++) magnitude[k] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) * norm;
            var row = f * Bins;
            for (var m = 0; m < Bins; m++)
            {
                var sum = 0f;
                for (var k = _bandStart![m]; k < _bandEnd![m]; k++) sum += magnitude[k] * fb[k * Bins + m];
                output[row + m] = MathF.Log(1 + 1000 * sum);
            }
        }
        return output;
    }

    private static (float[] beat, float[] down) Infer(InferenceSession session, float[] spect, int frames, CancellationToken ct)
    {
        var beat = new float[frames];
        var down = new float[frames];
        var filled = new bool[frames];
        var starts = new List<int>();
        for (var s = -Border; s < frames - Border; s += Chunk - 2 * Border) starts.Add(s);
        if (frames > Chunk && starts[^1] + Chunk > frames) starts[^1] = frames - Chunk + Border;
        var buffer = new float[Chunk * Bins];
        foreach (var start in starts)
        {
            ct.ThrowIfCancellationRequested();
            DenseTensor<float> input;
            if (start >= 0 && start + Chunk <= frames)
            {
                input = new DenseTensor<float>(spect.AsMemory(start * Bins, Chunk * Bins), [1, Chunk, Bins]);
            }
            else
            {
                Array.Clear(buffer);
                for (var t = 0; t < Chunk; t++)
                {
                    var src = start + t;
                    if (src < 0 || src >= frames) continue;
                    Array.Copy(spect, src * Bins, buffer, t * Bins, Bins);
                }
                input = new DenseTensor<float>(buffer.AsMemory(), [1, Chunk, Bins]);
            }
            using var results = session.Run((NamedOnnxValue[])[NamedOnnxValue.CreateFromTensor("spect", input)]);
            var beatOut = results.First(r => r.Name == "beat").AsTensor<float>();
            var downOut = results.First(r => r.Name == "downbeat").AsTensor<float>();
            for (var t = Border; t < Chunk - Border; t++)
            {
                var dst = start + t;
                if (dst < 0 || dst >= frames || filled[dst]) continue;
                beat[dst] = beatOut[0, t];
                down[dst] = downOut[0, t];
                filled[dst] = true;
            }
        }
        return (beat, down);
    }

    private static List<double> PickPeaks(float[] logits)
    {
        var peaks = new List<int>();
        for (var i = 0; i < logits.Length; i++)
        {
            if (logits[i] <= 0) continue;
            var isMax = true;
            for (var k = Math.Max(0, i - 3); k <= Math.Min(logits.Length - 1, i + 3); k++)
            {
                if (logits[k] <= logits[i]) continue;
                isMax = false;
                break;
            }
            if (isMax) peaks.Add(i);
        }
        var result = new List<double>(peaks.Count);
        for (var i = 0; i < peaks.Count; i++)
        {
            if (i + 1 < peaks.Count && peaks[i + 1] - peaks[i] <= 1)
            {
                result.Add((peaks[i] + peaks[i + 1]) / 2.0 / Fps);
                i++;
            }
            else
            {
                result.Add(peaks[i] / Fps);
            }
        }
        return result;
    }

    private static int NearestIndex(List<double> sorted, double value)
    {
        var index = sorted.BinarySearch(value);
        if (index >= 0) return index;
        index = ~index;
        if (index == 0) return 0;
        if (index >= sorted.Count) return sorted.Count - 1;
        return value - sorted[index - 1] <= sorted[index] - value ? index - 1 : index;
    }

    private static double Confidence(float[] logits, List<double> beats, List<double> sortedIbis, double median)
    {
        var salience = beats.Average(b => 1 / (1 + Math.Exp(-logits[Math.Clamp((int)Math.Round(b * Fps), 0, logits.Length - 1)])));
        var inner = sortedIbis.Skip(sortedIbis.Count / 10).Take(Math.Max(1, sortedIbis.Count * 8 / 10)).ToList();
        var average = inner.Average();
        var std = Math.Sqrt(inner.Sum(x => (x - average) * (x - average)) / Math.Max(1, inner.Count - 1));
        var regularity = Math.Clamp(1 - std / (0.1 * median), 0, 1);
        return Math.Round(0.5 * salience + 0.5 * regularity, 3);
    }
}
