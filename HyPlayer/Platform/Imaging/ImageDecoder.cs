using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace HyPlayer.Platform.Imaging;

public static class ImageDecoder
{
    private const uint SampleSize = 128;

    public static async Task<List<Vector4>> GetPixelColor(BitmapDecoder bitmapDecoder)
    {
        var width = bitmapDecoder.PixelWidth;
        var height = bitmapDecoder.PixelHeight;
        var scale = Math.Min(1d, SampleSize / (double)Math.Max(1, Math.Max(width, height)));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(width * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(height * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixelDataProvider = await bitmapDecoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = pixelDataProvider.DetachPixelData();
        var count = pixels.Length / 4;
        var vectors = new List<Vector4>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];
            if (a == 0) continue;
            var color = new Vector4(a, r, g, b);
            vectors.Add(color);
        }

        return vectors;
    }
}