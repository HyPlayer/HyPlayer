#nullable enable

using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.Text;
using HyPlayer.UI.Effects;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
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
    private bool _tail;
    private float _tailFocus;
    private LyricScalarTransition _focusTransition;

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
    private const float FocusFadeDurationMs = 300;

    public string? Text { get; set; }
    public override bool IsTextLine => true;
    internal int LastFrameGlyphRunCount => _focusedTextRenderer.LastFrameGlyphRunCount;
    public List<LyricTextToken> Tokens { get; set; } = [];
    public string? Transliteration { get; set; }
    public string? Translation { get; set; }
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };


    protected override bool RenderCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_layout is null) return true;
        if (IsActive)
            _focusedTextRenderer.Render(session, _layout, context);
        else if (_tail)
            _focusedTextRenderer.RenderTail(session, _layout, context, _tailFocus);
        else
            DrawIdle(session, _layout);
        return true;
    }

    private static void DrawIdle(CanvasDrawingSession session, LyricTextLayoutSnapshot layout)
    {
        if (layout.HasStaticContent) session.DrawImage(layout.StaticPersistCache, 0, 0, layout.SizePixelRect, 1);
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
        if (_layout is not null)
        {
            var focus = _focusTransition.Update(IsActive ? 1 : 0, context.CurrentLyricTime,
                IsActive ? 0 : FocusFadeDurationMs, context.IsSeek);
            if (!IsActive)
            {
                _tailFocus = focus > 0.001f ? focus : 0;
                var tail = _tailFocus > 0 || FocusedLyricTextRenderer.HasActiveEmphasis(_layout, context);
                if (_tail && !tail) _focusedTextRenderer.Dispose();
                _tail = tail;
                if (!tail)
                {
                    _idleLineCache ??= CreateIdleLineCache(session, _layout);
                    image = _idleLineCache;
                    return true;
                }
            }
        }

        image = null!;
        return false;
    }

    protected override void OnRenderingChanged(bool rendering)
    {
        if (rendering) return;
        ReleaseDrawingResources();
        _focusTransition = default;
        _focusedTextRenderer.Dispose();
        NativeClosable.Close(_idleLineCache);
        _idleLineCache = null;
    }

    protected override void OnKeyFrameCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_wasActive && !IsActive)
        {
            if (Hidden) _focusedTextRenderer.Dispose();
            else _tail = true;
        }
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

        if (KeepsLayoutAt(context.ItemWidth - TextPadding))
        {
            _canvasWidth = context.ItemWidth;
            _canvasHeight = context.ViewHeight;
            return;
        }

        OnTypographyChanged(session, context);
    }

    internal bool KeepsLayoutAt(float requestedWidth) =>
        _layout is not null && _layout.KeepsLayoutAt(requestedWidth);

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        _canvasWidth = context.ItemWidth;
        _canvasHeight = context.ViewHeight;
        ReleaseDrawingResources();
        _focusTransition = default;
        _focusedTextRenderer.Dispose();
        NativeClosable.Close(_idleLineCache);
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
        var idleSession = cache.CreateDrawingSession();
        try
        {
            if (layout.HasStaticContent)
                idleSession.DrawImage(layout.StaticPersistCache, 0, 0, layout.SizePixelRect, 1);
            DrawIdleLayer(idleSession, layout, layout.DefaultTransliterationPersistCache,
                layout.TransliterationLayout, 0, layout.DrawingOffsetY);
            DrawIdleLayer(idleSession, layout, layout.DefaultTextPersistCache, layout.TextLayout,
                layout.TextRenderActualTop, 0);
            DrawIdleLayer(idleSession, layout, layout.DefaultTranslationPersistCache, layout.TranslationLayout,
                layout.TranslationRenderActualTop, 0);
        }
        finally
        {
            NativeClosable.Close(idleSession);
        }

        return cache;
    }

    private static void DrawIdleLayer(CanvasDrawingSession session, LyricTextLayoutSnapshot layout,
        ICanvasImage? image, CanvasTextLayout? text, float top, float textTop)
    {
        if (image is null) return;
        if (text is null || layout.LayoutDrawOffsetX is not { } offsetX || MathF.Floor(top) != top)
        {
            session.DrawImage(image, 0, top, layout.SizePixelRect, 1);
            return;
        }

        var size = layout.SizePixelRect;
        var clip = session.CreateLayer(1, new Rect(size.X, size.Y + top, size.Width, size.Height));
        try
        {
            session.DrawTextLayout(text, offsetX, top + textTop, layout.IdleColor);
        }
        finally
        {
            NativeClosable.Close(clip);
        }
    }

    public override void Dispose()
    {
        _focusedTextRenderer.Dispose();
        NativeClosable.Close(_idleLineCache);
        _idleLineCache = null;
        _layout?.Dispose();
        _layout = null;
        base.Dispose();
    }
}
