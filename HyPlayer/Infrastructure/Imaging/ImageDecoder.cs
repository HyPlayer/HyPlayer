using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace HyPlayer.Infrastructure.Imaging
{
    public static class ImageDecoder
    {
        public static async Task<Dictionary<Vector3, int>> GetPixelColor(BitmapDecoder bitmapDecoder)
        {
            var pixelDataProvider = await bitmapDecoder.GetPixelDataAsync();
            var pixels = pixelDataProvider.DetachPixelData();
            var count = bitmapDecoder.PixelWidth * bitmapDecoder.PixelHeight;
            var vector = new Dictionary<Vector3, int>();
            for (int i = 0; i < count; i += 10)
            {
                var offset = i * 4;
                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];
                var a = pixels[offset + 3];
                if (a == 0) continue;
                var color = new Vector3(r, g, b);
                if (vector.TryGetValue(color, out int value))
                {
                    vector[color] = ++value;
                }
                else
                {
                    vector[color] = 1;
                }
            }
            return vector;
        }
    }
}
