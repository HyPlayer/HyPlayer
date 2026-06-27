#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public class DefaultTokenScanEffectRenderer : ITextHighlightEffectRenderer
{
    private const float LiftAmount = 3;
    private readonly Color _defaultColor = Color.FromArgb(255, 128, 128, 0);

    public void Render(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout,
        TextRenderFrame frame,
        Rect sizePixelRect,
        float textTop)
    {
        using var beforeGeometry = CreateGroupGeometry(session, frame.BeforeTokenBounds);
        using var afterGeometry = CreateGroupGeometry(session, frame.AfterTokenBounds);
        using var currentGeometry = CreateGroupGeometry(session, frame.CurrentTokenBounds);
        using var currentHighlightGeometry = CreateHighlightGeometry(session, frame.CurrentTokenProgress, frame.HighlightBounds);

        var beforeMatrix = Matrix3x2.CreateTranslation(0, textTop - LiftAmount);
        var afterMatrix = Matrix3x2.CreateTranslation(0, textTop);

        var textLayoutCommandList = new CanvasCommandList(session);
        using (var textLayoutSession = textLayoutCommandList.CreateDrawingSession())
        {
            if (frame.BeforeTokenBounds.Length > 0)
            {
                using (textLayoutSession.CreateLayer(1, beforeGeometry, beforeMatrix))
                {
                    textLayoutSession.DrawImage(layout.DefaultTextPersistCache, 0, textTop - LiftAmount, sizePixelRect, 1);
                }
            }

            if (frame.AfterTokenBounds.Length > 0)
            {
                using (textLayoutSession.CreateLayer(1, afterGeometry, afterMatrix))
                {
                    textLayoutSession.DrawImage(layout.DefaultTextPersistCache, 0, textTop, sizePixelRect, 0.3f);
                }
            }
        }

        var currentCommandList = new CanvasCommandList(session);
        using (var currentDrawingSession = currentCommandList.CreateDrawingSession())
        {
            using (currentDrawingSession.CreateLayer(1, currentGeometry, afterMatrix))
            {
                currentDrawingSession.DrawImage(layout.DefaultTextPersistCache, 0, textTop, sizePixelRect, 0.3f);
            }

            using (currentDrawingSession.CreateLayer(1, currentHighlightGeometry, afterMatrix))
            {
                currentDrawingSession.DrawImage(layout.DefaultTextPersistCache, 0, textTop);
            }
        }

        if (ShouldUseDisplacement(frame))
        {
            using var displacementMap = CreateDisplacementMap(session, currentGeometry, frame.CurrentTokenBounds, frame.CurrentTokenProgress, textTop);
            var displacementEffect = new DisplacementMapEffect
            {
                Source = currentCommandList,
                Displacement = displacementMap,
                XChannelSelect = EffectChannelSelect.Red,
                YChannelSelect = EffectChannelSelect.Green,
                Amount = LiftAmount * 2
            };
            session.DrawImage(displacementEffect, 0, 0);
        }
        else
        {
            var normalLift = frame.CurrentTokenIndex != -1
                ? -LiftAmount * Math.Clamp(frame.CurrentTokenProgress, 0, 1)
                : 0f;
            session.DrawImage(currentCommandList, 0, normalLift);
        }

        session.DrawImage(textLayoutCommandList);
    }

    protected static CanvasGeometry CreateGroupGeometry(ICanvasResourceCreator creator, IReadOnlyList<Rect> bounds)
    {
        if (bounds.Count == 0) return CanvasGeometry.CreateGroup(creator, []);
        return CanvasGeometry.CreateGroup(creator, [.. bounds.Select(t => CanvasGeometry.CreateRectangle(creator, t))]);
    }

    protected static CanvasGeometry CreateHighlightGeometry(ICanvasResourceCreator creator, float percentage, IReadOnlyList<Rect> bounds)
    {
        if (percentage <= 0 || bounds.Count == 0) return CanvasGeometry.CreateGroup(creator, []);

        var totalWidth = bounds.Sum(t => t.Width);
        var targetWidth = totalWidth * percentage;
        var geometries = new List<CanvasGeometry>();

        if (bounds.Count > 1)
        {
            foreach (var rect in bounds)
            {
                if (targetWidth <= 0) break;
                if (rect.Width <= targetWidth)
                {
                    geometries.Add(CanvasGeometry.CreateRectangle(creator, rect));
                    targetWidth -= (float)rect.Width;
                }
                else
                {
                    geometries.Add(CanvasGeometry.CreateRectangle(creator, new Rect(rect.X, rect.Y, targetWidth, rect.Height)));
                    targetWidth = 0;
                }
            }
        }
        else
        {
            var rect = bounds[0];
            if (rect.Width > 0)
            {
                return CanvasGeometry.CreateRectangle(
                    creator,
                    new Rect(rect.X, rect.Y, rect.Width * Math.Clamp(percentage, 0, 1), rect.Height));
            }
        }

        return CanvasGeometry.CreateGroup(creator, [.. geometries]);
    }

    private CanvasCommandList CreateDisplacementMap(
        CanvasDrawingSession session,
        CanvasGeometry currentGeometry,
        IReadOnlyList<Rect> currentBounds,
        float percentage,
        float textTop)
    {
        var displacementMap = new CanvasCommandList(session);
        using var displacementSession = displacementMap.CreateDrawingSession();
        displacementSession.Clear(_defaultColor);
        var gradientStops = new CanvasGradientStop[]
        {
            new() { Position = percentage - 0.5f, Color = Color.FromArgb(255, 128, 255, 0) },
            new() { Position = percentage, Color = Color.FromArgb(255, 128, 255, 0) },
            new() { Position = percentage + 0.5f, Color = Color.FromArgb(255, 128, 128, 0) }
        };
        using var gradientBrush = new CanvasLinearGradientBrush(displacementSession, gradientStops);
        var currentBoundsRect = GetUnionBounds(currentBounds);
        gradientBrush.StartPoint = new Vector2((float)currentBoundsRect.Left, 0);
        gradientBrush.EndPoint = new Vector2((float)currentBoundsRect.Width + (float)currentBoundsRect.Left, 0);
        displacementSession.FillGeometry(currentGeometry, 0, textTop, gradientBrush);
        return displacementMap;
    }

    private static Rect GetUnionBounds(IReadOnlyList<Rect> bounds)
    {
        if (bounds.Count == 0) return Rect.Empty;
        var left = bounds.Min(t => t.Left);
        var top = bounds.Min(t => t.Top);
        var right = bounds.Max(t => t.Right);
        var bottom = bounds.Max(t => t.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool ShouldUseDisplacement(TextRenderFrame frame)
    {
        return frame.CurrentToken is { Duration: >= 500, CharacterCount: >= 4 } && frame.CurrentTokenBounds.Length > 0;
    }
}
