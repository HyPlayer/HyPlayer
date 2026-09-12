using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.AI.MachineLearning;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using HyPlayer.Domain;
using Impressionist.Helpers;
using Impressionist.Quantizers;
using Impressionist.Selectors;

namespace HyPlayer.Platform.Imaging;

public static class ColorHelper
{
    private const int PattleImageSize = 224;
    private static readonly CelebiQuantizer Quantizer = new();
    private static readonly SemaphoreSlim PattleInferenceGate = new(1, 1);
    private static readonly object PattleSessionSync = new();
    private static Task<LearningModelSession>? _pattleSessionTask;

    public static async Task<Color> ExtractThemeColorFromStream(
        IRandomAccessStream stream,
        ColorGeneratorType generatorType = ColorGeneratorType.Auto)
    {
        if (generatorType == ColorGeneratorType.PattleCNN)
        {
            try
            {
                var palette = await ExtractPattlePaletteFromStream(stream);
                return AveragePalette(palette);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"PattleCNN theme extraction failed: {exception}");
                stream.Seek(0);
            }
        }

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var colors = await ImageDecoder.GetPixelColor(decoder);
        if (colors.Count == 0) return Colors.Black;
        var color = Vector4.Zero;
        foreach (var item in colors) color += item;
        color /= colors.Count;
        return Color.FromArgb((byte)color.X, (byte)color.Y, (byte)color.Z, (byte)color.W);
    }

    public static async Task<List<Vector3>> ExtractPaletteFromStream(
        IRandomAccessStream stream,
        ColorGeneratorType generatorType = ColorGeneratorType.Auto)
    {
        if (generatorType == ColorGeneratorType.PattleCNN)
        {
            try
            {
                return await ExtractPattlePaletteFromStream(stream);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"PattleCNN palette extraction failed: {exception}");
                stream.Seek(0);
            }
        }

        return await ExtractClassicPaletteFromStream(stream);
    }

    private static async Task<List<Vector3>> ExtractClassicPaletteFromStream(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var colors = await ImageDecoder.GetPixelColor(decoder);
        var inputs = colors.Select(t => new ArgbColor(t)).ToList();
        if (inputs.Count == 0) return [Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero];
        var quantized = Quantizer.Quantize(inputs, 32).Colors;
        var selector = new HctColorSelector();
        var scored = selector.SelectColors(quantized)
            .Select(t => new Vector3(t.Red, t.Green, t.Blue))
            .ToList();
        var result = new List<Vector3>(4);
        for (var i = 0; i < 4; i++)
            result.Add(scored[i % scored.Count]);
        return result;
    }

    private static async Task<List<Vector3>> ExtractPattlePaletteFromStream(IRandomAccessStream stream)
    {
        var input = await PreparePattleInputAsync(stream);
        var session = await GetPattleSessionAsync();
        await PattleInferenceGate.WaitAsync();
        try
        {
            var image = TensorFloat.CreateFromArray(
                new long[] { 1, 3, PattleImageSize, PattleImageSize }, input.Image);
            var anchors = TensorFloat.CreateFromArray(new long[] { 1, 4, 3 }, input.Anchors);
            var binding = new LearningModelBinding(session);
            binding.Bind("image", image);
            binding.Bind("anchors", anchors);
            var evaluation = await session.EvaluateAsync(binding, "PattleCNN");
            if (!evaluation.Outputs.TryGetValue("output", out var outputValue) ||
                outputValue is not TensorFloat output)
                throw new InvalidOperationException("PattleCNN model did not return an output tensor.");

            var values = output.GetAsVectorView();
            if (values.Count < 12) throw new InvalidOperationException("PattleCNN output tensor is too small.");
            var palette = new List<Vector3>(4);
            for (var i = 0; i < 4; i++)
                palette.Add(new Vector3(
                    Math.Clamp(values[i * 3], 0, 1) * 255f,
                    Math.Clamp(values[i * 3 + 1], 0, 1) * 255f,
                    Math.Clamp(values[i * 3 + 2], 0, 1) * 255f));
            return palette;
        }
        finally
        {
            PattleInferenceGate.Release();
        }
    }

    private static Task<LearningModelSession> GetPattleSessionAsync()
    {
        lock (PattleSessionSync)
            return _pattleSessionTask ??= LoadPattleSessionAsync();
    }

    private static async Task<LearningModelSession> LoadPattleSessionAsync()
    {
        var file = await StorageFile.GetFileFromApplicationUriAsync(
            new Uri("ms-appx:///Assets/PattleCNN.onnx"));
        var model = await LearningModel.LoadFromStorageFileAsync(file);
        return new LearningModelSession(model);
    }

    private static async Task<PattleInput> PreparePattleInputAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = PattleImageSize,
            ScaledHeight = PattleImageSize,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixelDataProvider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = pixelDataProvider.DetachPixelData();
        var pixelCount = PattleImageSize * PattleImageSize;
        var image = new float[pixelCount * 3];
        var points = new Vector3[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var point = new Vector3(pixels[offset], pixels[offset + 1], pixels[offset + 2]) / 255f;
            points[i] = point;
            image[i] = point.X;
            image[pixelCount + i] = point.Y;
            image[pixelCount * 2 + i] = point.Z;
        }

        return new PattleInput(image, BuildAnchors(points));
    }

    private static float[] BuildAnchors(IReadOnlyList<Vector3> points)
    {
        var centers = new[]
        {
            points[0], points[points.Count / 3], points[points.Count * 2 / 3], points[points.Count - 1]
        };
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var sums = new Vector3[4];
            var counts = new int[4];
            foreach (var point in points)
            {
                var nearest = 0;
                var distance = Vector3.DistanceSquared(point, centers[0]);
                for (var i = 1; i < centers.Length; i++)
                {
                    var candidateDistance = Vector3.DistanceSquared(point, centers[i]);
                    if (candidateDistance < distance)
                    {
                        nearest = i;
                        distance = candidateDistance;
                    }
                }

                sums[nearest] += point;
                counts[nearest]++;
            }

            for (var i = 0; i < centers.Length; i++)
                if (counts[i] > 0)
                    centers[i] = sums[i] / counts[i];
        }

        Array.Sort(centers, static (left, right) => Luminance(left).CompareTo(Luminance(right)));
        var result = new float[12];
        for (var i = 0; i < centers.Length; i++)
        {
            result[i * 3] = centers[i].X;
            result[i * 3 + 1] = centers[i].Y;
            result[i * 3 + 2] = centers[i].Z;
        }

        return result;
    }

    private static Color AveragePalette(IReadOnlyList<Vector3> palette)
    {
        var average = Vector3.Zero;
        foreach (var color in palette) average += color;
        average /= palette.Count;
        return Color.FromArgb(255, (byte)Math.Clamp(average.X, 0, 255),
            (byte)Math.Clamp(average.Y, 0, 255), (byte)Math.Clamp(average.Z, 0, 255));
    }

    private static float Luminance(Vector3 color) =>
        color.X * 0.2126f + color.Y * 0.7152f + color.Z * 0.0722f;

    private readonly record struct PattleInput(float[] Image, float[] Anchors);

    public static bool RGBVectorLStarIsDark(this Vector3 rgb)
    {
        var limitedColor = rgb / 255f;
        var y = 0.2126f * ChannelToLin(limitedColor.X) + 0.7152f * ChannelToLin(limitedColor.Y) +
                0.0722f * ChannelToLin(limitedColor.Z);
        var lStar = YToLStar(y);
        return lStar <= 50;
    }

    public static float ChannelToLin(float value)
    {
        if (value <= 0.04045f) return value / 12.92f;

        return (float)Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    public static float YToLStar(float y)
    {
        if (y <= 216f / 24389f)
            return y * (24389f / 27f);

        return (float)Math.Pow(y, 1f / 3f) * 116f - 16f;
    }
}
