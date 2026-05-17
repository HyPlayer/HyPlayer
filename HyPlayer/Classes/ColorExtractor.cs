using CommunityToolkit.Mvvm.DependencyInjection;
using Impressionist.Abstractions;
using Impressionist.Implementations;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace HyPlayer.Classes
{
    public static class ColorExtractor
    {
        public static async Task<Color> ExtractColorFromStream(IRandomAccessStream stream)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var colors = await ImageDecoder.GetPixelColor(decoder);
            ThemeColorResult color;

            var setting = Ioc.Default.GetRequiredService<Setting>();

            if (setting.ColorGeneratorType is ColorGeneratorType.OctTree)
            {
                color = await PaletteGenerators.OctTreePaletteGenerator.CreateThemeColor(colors, true);

            }
            else
            {
                color = await PaletteGenerators.KMeansPaletteGenerator.CreateThemeColor(colors, true, true);
            }
            return Color.FromArgb(255, (byte)color.Color.X, (byte)color.Color.Y, (byte)color.Color.Z);
        }
    }
}
