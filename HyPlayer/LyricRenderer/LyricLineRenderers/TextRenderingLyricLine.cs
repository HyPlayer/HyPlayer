#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.Text;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

public partial class TextRenderingLyricLine : RenderingLyricLine
{
    private readonly ILyricTextLayouter _layouter;
    private readonly FocusedLyricTextRenderer _focusedTextRenderer = new();
    private CanvasCommandList? _idleLineCache;

    private float _canvasWidth;
    private float _canvasHeight;
    private LyricTextLayoutSnapshot? _layout;

    private TextAlignment _cachedAlignment;
    private float _cachedLyricFontSize;
    private float _cachedTransliterationFontSize;
    private float _cachedTranslationFontSize;
    private string? _cachedFontFamily;
    private Color _cachedIdleColor;
    private Color _cachedFocusingColor;
    private bool _wasActive;
    private bool _emphasisTail;

    public TextRenderingLyricLine()
        : this(new Win2DLyricTextLayouter())
    {
    }

    public TextRenderingLyricLine(
        ILyricTextLayouter layouter)
    {
        ArgumentNullException.ThrowIfNull(layouter);
        _layouter = layouter;
    }

    public const float TextPadding = 16;
    public const float LiftAmount = 3;

    public string? Text { get; set; }
    public override bool IsTextLine => true;
    public List<LyricTextToken> Tokens { get; set; } = [];
    public string? Transliteration { get; set; }
    public string? Translation { get; set; }
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };


    protected override bool RenderCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_layout is null) return true;
        if (IsActive)
            _focusedTextRenderer.Render(session, _layout, context);
        else if (_emphasisTail)
            _focusedTextRenderer.RenderEmphasisTail(session, _layout, context);
        else
            DrawIdle(session, _layout);
        return true;
    }

    private static void DrawIdle(CanvasDrawingSession session, LyricTextLayoutSnapshot layout)
    {
        session.DrawImage(layout.StaticPersistCache, 0, 0, layout.SizePixelRect, 1);
        if (layout.DefaultTransliterationPersistCache is not null)
            session.DrawImage(layout.DefaultTransliterationPersistCache, 0, 0, layout.SizePixelRect, 1);
        session.DrawImage(layout.DefaultTextPersistCache, 0, layout.TextRenderActualTop, layout.SizePixelRect, 1);
        if (layout.DefaultTranslationPersistCache is not null)
            session.DrawImage(layout.DefaultTranslationPersistCache, 0, layout.TranslationRenderActualTop, layout.SizePixelRect, 1);
    }

    protected override bool TryGetStaticSourceImage(
        CanvasDrawingSession session,
        RenderContext context,
        out ICanvasImage image)
    {
        if (!IsActive && _layout is not null)
        {
            var emphasisTail = FocusedLyricTextRenderer.HasActiveEmphasis(_layout, context);
            if (_emphasisTail && !emphasisTail) _focusedTextRenderer.Dispose();
            _emphasisTail = emphasisTail;
            if (!emphasisTail)
            {
                _idleLineCache ??= CreateIdleLineCache(session, _layout);
                image = _idleLineCache;
                return true;
            }
        }

        image = null!;
        return false;
    }

    protected override void OnRenderingChanged(bool rendering)
    {
        if (rendering) return;
        ReleaseDrawingResources();
        _focusedTextRenderer.Dispose();
        _idleLineCache?.Dispose();
        _idleLineCache = null;
    }

    protected override void OnKeyFrameCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_wasActive && !IsActive) _focusedTextRenderer.Dispose();
        _wasActive = IsActive;
        if (_canvasWidth == 0.0f) return;
        if (_layout is null)
            OnTypographyChanged(session, context);
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {
        if (HiddenOnBlur && !IsActive)
        {
            Hidden = true;
        }

        _canvasWidth = context.ItemWidth;
        _canvasHeight = context.ViewHeight;
        OnKeyFrameCore(session, context);
        OnTypographyChanged(session, context);
    }

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        ReleaseDrawingResources();
        _focusedTextRenderer.Dispose();
        _idleLineCache?.Dispose();
        _idleLineCache = null;
        _cachedAlignment = TypographySelector(t => t?.Alignment, context)!.Value;
        _cachedLyricFontSize = TypographySelector(t => t?.LyricFontSize, context)!.Value;
        _cachedTransliterationFontSize = TypographySelector(t => t?.TransliterationFontSize, context)!.Value;
        _cachedTranslationFontSize = TypographySelector(t => t?.TranslationFontSize, context)!.Value;
        _cachedFontFamily = TypographySelector(t => t?.Font, context);
        _cachedIdleColor = TypographySelector(t => t?.IdleColor, context)!.Value;
        _cachedFocusingColor = TypographySelector(t => t?.FocusingColor, context)!.Value;

        _layout?.Dispose();
        _layout = _layouter.CreateLayout(new LyricTextLayoutRequest
        {
            Session = session,
            Context = context,
            Typography = Typography ?? context.PreferTypography,
            Text = Text ?? string.Empty,
            Tokens = Tokens,
            LineStartTime = StartTime,
            LineEndTime = EndTime,
            Translation = Translation,
            Transliteration = Transliteration,
            HiddenOnBlur = HiddenOnBlur,
            TextPadding = TextPadding,
            LiftAmount = LiftAmount,
            IdleColor = _cachedIdleColor,
            FocusingColor = _cachedFocusingColor,
            CanvasHeight = _canvasHeight,
            Alignment = _cachedAlignment,
            LyricFontSize = _cachedLyricFontSize,
            TranslationFontSize = _cachedTranslationFontSize,
            TransliterationFontSize = _cachedTransliterationFontSize,
            FontFamily = _cachedFontFamily
        });

        RenderingHeight = _layout.RenderingHeight;
        RenderingWidth = _layout.RenderingWidth;
    }

    private static CanvasCommandList CreateIdleLineCache(
        CanvasDrawingSession session,
        LyricTextLayoutSnapshot layout)
    {
        var cache = new CanvasCommandList(session);
        using var idleSession = cache.CreateDrawingSession();
        idleSession.DrawImage(layout.StaticPersistCache, 0, 0, layout.SizePixelRect, 1);
        if (layout.DefaultTransliterationPersistCache is not null)
            idleSession.DrawImage(layout.DefaultTransliterationPersistCache, 0, 0, layout.SizePixelRect, 1);
        idleSession.DrawImage(layout.DefaultTextPersistCache, 0, layout.TextRenderActualTop, layout.SizePixelRect, 1);
        if (layout.DefaultTranslationPersistCache is not null)
        {
            idleSession.DrawImage(
                layout.DefaultTranslationPersistCache,
                0,
                layout.TranslationRenderActualTop,
                layout.SizePixelRect,
                1);
        }

        return cache;
    }

    public override void Dispose()
    {
        _focusedTextRenderer.Dispose();
        _idleLineCache?.Dispose();
        _idleLineCache = null;
        _layout?.Dispose();
        _layout = null;
        base.Dispose();
    }
}
