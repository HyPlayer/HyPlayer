using CommunityToolkit.Mvvm.DependencyInjection;
using Impressionist;
using Impressionist.Helpers;
using Impressionist.Quantizers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace HyPlayer.Infrastructure.Imaging
{
    public static class ColorExtractor
    {
        private static CelebiQuantizer _quantizer = Ioc.Default.GetRequiredService<CelebiQuantizer>();
        public static async Task<Color> ExtractThemeColorFromStream(IRandomAccessStream stream)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            var color = Vector4.Zero;
            var count = colors.Count;
            foreach(var item in colors)
            {
                color += item;
            }
            color /= count;
            return Color.FromArgb((byte)color.X, (byte)color.Y, (byte)color.Z, (byte)color.W);
        }
        public static async Task<List<Vector3>> ExtractPaletteFromStream(IRandomAccessStream stream, int count)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            var inputs = colors.Select(t => new ArgbColor(t)).ToList();
            var quantized = _quantizer.Quantize(inputs, count).Colors;
            var scored = Score.CalculateScore(quantized)
            .Select(t => new Vector3(t.Red, t.Green, t.Blue))
            .ToList();
            var result = new List<Vector3>();
            var originalCount = scored.Count;
            for (int i = 0; i < count; i++)
            {
                // You know, it is always hard to fullfill a palette when you have no enough colors. So please forgive me when placing the same color over and over again.
                result.Add(scored[i % originalCount]);
            }
            return result;
        }
    }
}
