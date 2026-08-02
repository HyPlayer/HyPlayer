using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace HyPlayer.Platform.Imaging;

public static class ImageDecoder
{
    public static async Task<List<Vector4>> GetPixelColor(BitmapDecoder bitmapDecoder)
    {
        var pixelDataProvider = await bitmapDecoder.GetPixelDataAsync();
        var pixels = pixelDataProvider.DetachPixelData();
        var count = bitmapDecoder.PixelWidth * bitmapDecoder.PixelHeight;
        var vectors = new List<Vector4>();
        for (var i = 0; i < count; i += 10)
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