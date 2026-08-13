#nullable enable

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Text;

public static class GlyphRunDrawHelper
{
    public static void DrawCluster(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state,
        Rect? clipRect = null)
    {
        if (state.SkipDraw)
        {
            return;
        }

        var alpha = (byte)Math.Round(state.Color.A * Math.Clamp(state.Opacity, 0, 1));
        if (alpha == 0)
        {
            return;
        }

        brush.Color = Color.FromArgb(alpha, state.Color.R, state.Color.G, state.Color.B);
        var originalTransform = session.Transform;

        try
        {
            if (Math.Abs(state.Scale - 1) > 0.001f ||
                Math.Abs(state.ScaleX - 1) > 0.001f ||
                Math.Abs(state.ScaleY - 1) > 0.001f ||
                Math.Abs(state.Rotation) > 0.001f)
            {
                session.Transform =
                    Matrix3x2.CreateScale(state.Scale * state.ScaleX, state.Scale * state.ScaleY, state.Origin) *
                    Matrix3x2.CreateRotation(MathF.PI * state.Rotation / 180f, state.Origin) *
                    originalTransform;
            }

            if (clipRect is { } rect)
            {
                using var layer = session.CreateLayer(1, rect);
                DrawTransformedClusterContent(session, brush, state);
            }
            else
            {
                DrawTransformedClusterContent(session, brush, state);
            }
        }
        finally
        {
            session.Transform = originalTransform;
        }
    }

    private static void DrawTransformedClusterContent(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
    {
        if (Math.Abs(state.RotationX) <= 0.001f && Math.Abs(state.RotationY) <= 0.001f)
        {
            DrawClusterContent(session, brush, state);
            return;
        }

        using var source = new CanvasCommandList(session);
        using (var sourceSession = source.CreateDrawingSession())
        {
            using var sourceBrush = new CanvasSolidColorBrush(sourceSession, brush.Color);
            var localState = state;
            localState.Origin = Vector2.Zero;
            localState.RotationX = 0;
            localState.RotationY = 0;
            DrawClusterContent(sourceSession, sourceBrush, localState);
        }

        var advance = 0f;
        foreach (var glyph in state.Glyphs) advance += glyph.Advance;
        var center = new Vector3(advance / 2f, -state.FontSize / 2f, 0);
        var perspective = Matrix4x4.Identity;
        perspective.M34 = 1f / Math.Max(state.PerspectiveDepth, 1);
        using var transform = new Transform3DEffect
        {
            Source = source,
            TransformMatrix =
                Matrix4x4.CreateTranslation(-center) *
                Matrix4x4.CreateRotationX(MathF.PI * state.RotationX / 180f) *
                Matrix4x4.CreateRotationY(MathF.PI * state.RotationY / 180f) *
                perspective *
                Matrix4x4.CreateTranslation(center)
        };
        session.DrawImage(transform, state.Origin);
    }

    private static void DrawClusterContent(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
    {
        DrawShadow(session, brush, state);
        DrawGlow(session, brush, state);
        DrawStroke(session, brush, state);
        if (state.BlurRadius > 0.001f)
        {
            DrawBlurredGlyph(session, brush, state);
        }
        else
        {
            DrawGlyphRun(session, state, brush);
        }
    }

    private static void DrawShadow(CanvasDrawingSession session, CanvasSolidColorBrush brush, LyricGlyphDrawState state)
    {
        if (state.ShadowOpacity <= 0.001f) return;
        var originalColor = brush.Color;
        var originalOrigin = state.Origin;
        brush.Color = Color.FromArgb(
            (byte)Math.Round(state.ShadowColor.A * Math.Clamp(state.ShadowOpacity, 0, 1)),
            state.ShadowColor.R, state.ShadowColor.G, state.ShadowColor.B);
        state.Origin += state.ShadowOffset;
        if (state.ShadowBlur > 0.001f)
        {
            var copy = state;
            copy.BlurRadius = state.ShadowBlur;
            DrawBlurredGlyph(session, brush, copy);
        }
        else DrawGlyphRun(session, state, brush);
        state.Origin = originalOrigin;
        brush.Color = originalColor;
    }

    private static void DrawGlow(CanvasDrawingSession session, CanvasSolidColorBrush brush, LyricGlyphDrawState state)
    {
        if (state.GlowOpacity <= 0.001f || state.GlowRadius <= 0.001f) return;
        var originalColor = brush.Color;
        brush.Color = Color.FromArgb(
            (byte)Math.Round(state.GlowColor.A * Math.Clamp(state.GlowOpacity, 0, 1)),
            state.GlowColor.R, state.GlowColor.G, state.GlowColor.B);
        var copy = state;
        copy.BlurRadius = state.GlowRadius;
        DrawBlurredGlyph(session, brush, copy);
        brush.Color = originalColor;
    }

    private static void DrawStroke(CanvasDrawingSession session, CanvasSolidColorBrush brush, LyricGlyphDrawState state)
    {
        if (state.StrokeWidth <= 0.001f) return;
        var originalColor = brush.Color;
        var originalOrigin = state.Origin;
        brush.Color = state.StrokeColor;
        var width = Math.Clamp(state.StrokeWidth, 0, 8);
        ReadOnlySpan<Vector2> directions =
        [
            new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
            new(-0.707f, -0.707f), new(0.707f, -0.707f),
            new(-0.707f, 0.707f), new(0.707f, 0.707f)
        ];
        foreach (var direction in directions)
        {
            state.Origin = originalOrigin + direction * width;
            DrawGlyphRun(session, state, brush);
        }
        state.Origin = originalOrigin;
        brush.Color = originalColor;
    }

    private static void DrawBlurredGlyph(
        CanvasDrawingSession session,
        CanvasSolidColorBrush brush,
        LyricGlyphDrawState state)
    {
        using var commandList = new CanvasCommandList(session);
        using (var glyphSession = commandList.CreateDrawingSession())
        {
            using var glyphBrush = new CanvasSolidColorBrush(glyphSession, brush.Color);
            var localState = state;
            localState.Origin = Vector2.Zero;
            DrawGlyphRun(glyphSession, localState, glyphBrush);
        }

        using var blur = new GaussianBlurEffect
        {
            Source = commandList,
            BlurAmount = Math.Clamp(state.BlurRadius, 0, 250),
            BorderMode = EffectBorderMode.Soft
        };
        session.DrawImage(blur, state.Origin);
    }

    private static void DrawGlyphRun(
        CanvasDrawingSession session,
        LyricGlyphDrawState state,
        CanvasSolidColorBrush brush)
    {
        session.DrawGlyphRun(
            state.Origin,
            state.FontFace,
            state.FontSize,
            state.Glyphs,
            state.IsSideways,
            state.BidiLevel,
            brush,
            state.MeasuringMode,
            state.LocaleName,
            state.TextString,
            state.ClusterMap,
            state.CharacterIndex);
    }
}
