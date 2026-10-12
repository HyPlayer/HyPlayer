using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.LyricLineRenderers;
using HyPlayer.LyricRenderer.Text;
using Microsoft.Graphics.Canvas;
using System.Numerics;
using TUnit.Core;
using Windows.UI;

namespace HyPlayer.Playback.Tests;

public sealed class DirectLyricDrawingTests
{
    [Test]
    public void Defaults_ShouldUseSoftDepthAndSoftLift()
    {
        var settings = LyricRenderSettings.Default;
        if (settings.Line.Preset != LyricLinePreset.SoftDepth || settings.Line.MaximumBlur != 16 ||
            settings.Focused.Preset != LyricHighlightPreset.SoftLift || settings.Focused.LiftHeight != 2 ||
            settings.Focused.FeatherDip != 40)
            throw new InvalidOperationException("Default appearances changed.");
    }

    [Test]
    public void ProgressAndTransitions_ShouldAllocateNothingAfterWarmup()
    {
        var transition = new LyricScalarTransition();
        var timeline = FocusedLineRevealTimeline.Create(new FocusedLineRevealSpan[] { new(0, 1000, 0, 100), new(1000, 2000, 100, 200) }, 0, 200, false);
        float Compute(int i)
        {
            var raw = FocusedTextProgress.GetTimedProgress(i % 2000, 0, 1000, 0, 250, GlyphLiftMotion.Hold);
            return FocusedTextProgress.GetLiftProgress(raw, 1000, 1, 3, 0.7f, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold)
                   + timeline.Sample(i % 2000).Progress + transition.Update(i % 2, i, 500, false);
        }
        var total = 0f;
        for (var i = 0; i < 10000; i++) total += Compute(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100000; i++) total += Compute(i);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0 || !float.IsFinite(total)) throw new InvalidOperationException($"Numeric frame loop allocated {allocated} bytes.");
    }

    [Test]
    public void Win2D_ShouldRenderAllPresetsAndRestoreSessionTransform()
    {
        Win2DTestHost.Initialize();
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, 800, 600, 96);
        var context = new RenderContext { ItemWidth = 600, ViewWidth = 800, ViewHeight = 600, CurrentKeyframe = 0, CurrentLyricTime = 1400,
            PreferTypography = new() { TransliterationFontSize = 14 } };
        using var line = new TextRenderingLyricLine
        {
            Text = "沿着光亮缓缓向前 office العربية hello",
            Translation = "Translation remains visible throughout the scan",
            Transliteration = "yan zhe guang liang huan huan xiang qian",
            StartTime = 0, EndTime = 4000
        };
        using (var session = target.CreateDrawingSession())
        {
            line.OnRenderSizeChanged(session, context);
            line.OnKeyFrame(session, context);
            foreach (var preset in Enum.GetValues<LyricLinePreset>())
            foreach (var reveal in Enum.GetValues<HighlightRevealMode>())
            {
                context.Settings = new(LyricLineEffectSettings.ForPreset(preset), new() { RevealMode = reveal });
                var transform = Matrix3x2.CreateTranslation(8, 12);
                session.Transform = transform;
                line.Render(session, new() { X = 40, Y = 60 }, context);
                if (session.Transform != transform) throw new InvalidOperationException("Line render leaked its transform.");
                if (line.LastFrameCommandListCount > 1) throw new InvalidOperationException("Spatial effects recorded the line more than once.");
            }
            // Inactive line exercises cached blur, perspective and a return from a seek.
            context.CurrentKeyframe = 5000;
            context.CurrentLyricTime = 5000;
            context.IsSeek = true;
            context.CurrentLyricLine = new TextRenderingLyricLine { GroupIndex = 0 };
            line.GroupIndex = 2;
            line.OnKeyFrame(session, context);
            context.Settings = new(LyricLineEffectSettings.ForPreset(LyricLinePreset.Layered), new());
            line.Render(session, new() { X = 40, Y = 200 }, context);
            if (line.LastFrameCommandListCount != 0) throw new InvalidOperationException("Inactive line did not reuse its content cache.");
        }
        if (!target.GetPixelBytes().Any(value => value != 0)) throw new InvalidOperationException("Direct drawing produced an empty image.");
    }

    [Test]
    public void DirectGradient_ShouldKeepCjkTailPendingAndTranslationVisible()
    {
        Win2DTestHost.Initialize();
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, 800, 300, 96);
        var context = new RenderContext { ItemWidth = 700, ViewWidth = 800, ViewHeight = 300, CurrentLyricTime = 2800 };
        var tokens = "一二三四五六七八九十".Select((text, i) => new LyricTextToken(text.ToString(), i * 400, (i + 1) * 400, null)).ToArray();
        LyricTextLayoutSnapshot layout;
        using (var session = target.CreateDrawingSession())
            layout = new Win2DLyricTextLayouter().CreateLayout(new()
            {
                Session = session, Context = context, Typography = RenderTypography.Default,
                Text = string.Concat(tokens.Select(t => t.Text)), Tokens = tokens, LineStartTime = 0, LineEndTime = 4000,
                Translation = "Translation", TextPadding = 16, LiftAmount = 3, IdleColor = Colors.Blue,
                FocusingColor = Colors.Red, CanvasHeight = 300, LyricFontSize = 40, TranslationFontSize = 20,
                TransliterationFontSize = 14, FontFamily = "Microsoft YaHei UI", Alignment = Windows.UI.Xaml.TextAlignment.Left
            });
        using (layout)
        using (var renderer = new FocusedLyricTextRenderer())
        {
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Colors.Black);
                renderer.Render(session, layout, context);
            }
            var pixels = target.GetPixelBytes();
            var tail = layout.LyricGlyphClusters[^1];
            var blue = 0;
            var red = 0;
            for (var y = Math.Max(0, (int)tail.VisualTop); y < Math.Min(300, (int)Math.Ceiling(tail.VisualBottom)); y++)
            for (var x = Math.Max(0, (int)tail.VisualLeft); x < Math.Min(800, (int)Math.Ceiling(tail.VisualRight)); x++)
            {
                var offset = (y * 800 + x) * 4;
                blue += pixels[offset]; red += pixels[offset + 2];
            }
            if (blue <= 0 || red > blue / 100) throw new InvalidOperationException($"CJK tail was highlighted early: blue={blue}, red={red}.");
            var translationBlue = 0;
            for (var y = (int)layout.TranslationRenderActualTop; y < Math.Min(300, (int)layout.TranslationRenderActualTop + 30); y++)
            for (var x = 0; x < 200; x++) translationBlue += pixels[(y * 800 + x) * 4];
            if (translationBlue <= 0) throw new InvalidOperationException("Translation disappeared during the scan.");
        }
    }
}
