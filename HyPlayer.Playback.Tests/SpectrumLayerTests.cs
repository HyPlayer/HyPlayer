using System.Numerics;
using HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;
using HyPlayer.UWP.Chopin.Utils;
using Microsoft.Graphics.Canvas;
using TUnit.Core;
using Windows.UI;

namespace HyPlayer.Playback.Tests;

public sealed class SpectrumLayerTests
{
    [Test]
    public void Win2D_Contour_ShouldStayInsidePeaksAndRenderAtDifferentSizes()
    {
        Win2DTestHost.Initialize();
        using var device = new CanvasDevice();
        foreach (var (width, height, dpi) in new[] { (800, 600, 96), (1920, 1080, 120), (600, 400, 192) })
        {
            var points = new Vector2[FFTProcessor.DisplayBandCount];
            var maximumHeight = height / 2f - 1;
            for (var i = 0; i < points.Length; i++)
                points[i] = new Vector2((float)width * i / (points.Length - 1),
                    height - (i == 0 || i == points.Length - 1 ? 0 : i % 2 == 0 ? maximumHeight : maximumHeight * 0.1f));

            using var geometry = SpectrumLayer.CreateGeometry(device, points, height);
            var bounds = geometry.ComputeBounds();
            if (bounds.Left < -0.01 || bounds.Right > width + 0.01 ||
                bounds.Top < height - maximumHeight - 0.01 || bounds.Bottom > height + 0.01)
                throw new InvalidOperationException($"Contour overshot the viewport or peaks: {bounds}.");
            var peak = points[20];
            if (!geometry.FillContainsPoint(peak + new Vector2(0, 0.5f)) ||
                geometry.FillContainsPoint(peak - new Vector2(0, 0.5f)))
                throw new InvalidOperationException("The continuous contour changed a band's peak height.");

            using var target = new CanvasRenderTarget(device, width, height, dpi);
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Colors.Transparent);
                session.FillGeometry(geometry, Colors.White);
            }

            if (!target.GetPixelBytes().Any(value => value != 0))
                throw new InvalidOperationException("Contour did not render.");
        }
    }
}
