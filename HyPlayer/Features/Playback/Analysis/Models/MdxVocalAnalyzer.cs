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

public sealed class MdxVocalAnalyzer : IVocalAnalyzer
{
    public const int SampleRate = 44100;
    private const int NFft = 7680;
    private const int Hop = 1024;
    private const int DimF = 3072;
    private const int DimT = 256;
    private const int Margin = 16;

    private readonly ModelManager _models;

    public MdxVocalAnalyzer(ModelManager models)
    {
        _models = models;
    }

    public string Id => "mdx";
    public bool IsAvailable => _models.IsUsable(ModelCatalog.VocalId);

    public async Task<VocalActivity?> AnalyzeAsync(float[] stereo, int sampleRate, double offsetSeconds, CancellationToken ct)
    {
        if (sampleRate != SampleRate || stereo.Length < SampleRate * 2 * 3) return null;
        using var lease = await _models.AcquireSessionAsync(ModelCatalog.VocalId);
        var (vocal, mix) = await Task.Run(() => Separate(lease.Session, stereo, ct), ct);
        return Measure(vocal, mix, offsetSeconds);
    }

    private static (double[] vocal, double[] mix) Separate(InferenceSession session, float[] stereo, CancellationToken ct)
    {
        var count = stereo.Length / 2;
        var peak = 0f;
        foreach (var sample in stereo) peak = Math.Max(peak, Math.Abs(sample));
        var gain = peak > 0.9f ? 0.9f / peak : 1f;
        var frames = count / Hop + 1;
        var fft = new BluesteinFft(NFft);
        var window = new float[NFft];
        for (var i = 0; i < NFft; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft)) * gain;
        var frameInput = new float[NFft];
        var re = new float[DimF];
        var im = new float[DimF];
        var vocal = new double[frames];
        var mix = new double[frames];
        var block = new float[4 * DimF * DimT];
        var step = DimT - 2 * Margin;
        for (var start = 0; ; start += step)
        {
            var last = start + DimT >= frames;
            Array.Clear(block);
            for (var t = 0; t < DimT && start + t < frames; t++)
            {
                if ((t & 31) == 0) ct.ThrowIfCancellationRequested();
                var center = (start + t) * Hop - NFft / 2;
                for (var channel = 0; channel < 2; channel++)
                {
                    for (var i = 0; i < NFft; i++)
                    {
                        var index = center + i;
                        if (index < 0) index = -index;
                        if (index >= count) index = 2 * (count - 1) - index;
                        index = Math.Clamp(index, 0, count - 1);
                        frameInput[i] = stereo[index * 2 + channel] * window[i];
                    }
                    fft.Forward(frameInput, re, im, DimF);
                    var rePlane = channel * 2 * DimF;
                    var imPlane = rePlane + DimF;
                    for (var k = 3; k < DimF; k++)
                    {
                        block[(rePlane + k) * DimT + t] = re[k];
                        block[(imPlane + k) * DimT + t] = im[k];
                    }
                }
            }
            ct.ThrowIfCancellationRequested();
            var input = new DenseTensor<float>(block.AsMemory(), [1, 4, DimF, DimT]);
            using var results = session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
            var tensor = results.First(r => r.Name == "output").AsTensor<float>();
            ReadOnlySpan<float> output = tensor is DenseTensor<float> dense ? dense.Buffer.Span : tensor.ToArray();
            var keepFrom = start == 0 ? 0 : Margin;
            var keepTo = last ? DimT : DimT - Margin;
            for (var t = keepFrom; t < keepTo; t++)
            {
                var frame = start + t;
                if (frame >= frames) break;
                double v = 0, m = 0;
                for (var plane = 0; plane < 4 * DimF; plane++)
                {
                    if (plane % DimF < 3) continue;
                    var index = plane * DimT + t;
                    var o = output[index];
                    var x = block[index];
                    v += o * o;
                    m += x * x;
                }
                vocal[frame] = v;
                mix[frame] = m;
            }
            if (last) break;
        }
        return (vocal, mix);
    }

    private static VocalActivity Measure(double[] vocal, double[] mix, double offsetSeconds)
    {
        var secondsPerFrame = Hop / (double)SampleRate;
        var radius = (int)Math.Round(0.2 / secondsPerFrame);
        var smoothVocal = Smooth(vocal, radius);
        var smoothMix = Smooth(mix, radius);
        var loudest = smoothMix.DefaultIfEmpty(0).Max();
        var floor = loudest * Math.Pow(10, -20 / 10.0);
        var values = new double[(int)(vocal.Length * secondsPerFrame * VocalActivity.Rate)];
        for (var i = 0; i < values.Length; i++)
        {
            var frame = Math.Min(vocal.Length - 1, (int)(i / VocalActivity.Rate / secondsPerFrame));
            var ratio = smoothMix[frame] > floor ? smoothVocal[frame] / (smoothMix[frame] + 1e-12) : 0;
            values[i] = Math.Round(Math.Min(1, ratio), 3);
        }
        return new VocalActivity(offsetSeconds, values);
    }

    private static double[] Smooth(double[] values, int radius)
    {
        var result = new double[values.Length];
        double sum = 0;
        var count = 0;
        for (var i = 0; i < values.Length + radius; i++)
        {
            if (i < values.Length)
            {
                sum += values[i];
                count++;
            }
            if (i - 2 * radius - 1 >= 0)
            {
                sum -= values[i - 2 * radius - 1];
                count--;
            }
            var center = i - radius;
            if (center >= 0 && center < values.Length) result[center] = sum / Math.Max(1, count);
        }
        return result;
    }
}
