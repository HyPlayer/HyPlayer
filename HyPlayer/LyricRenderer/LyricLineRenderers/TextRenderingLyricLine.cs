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

public class TextRenderingLyricLine : RenderingLyricLine
{
    private const long ScaleAnimationDuration = 500;

    private readonly CustomElasticEase _elasticEase = new() { Springiness = 6 };
    private readonly ILyricTextLayouter _layouter;
    private readonly ITextProgressResolver _progressResolver;
    private readonly ITextHighlightEffectRenderer _highlightEffectRenderer;
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
    private Color _cachedFocusingColor;
    private bool _wasActive;

    public TextRenderingLyricLine()
        : this(new Win2DLyricTextLayouter(), new DefaultTextProgressResolver(), new DefaultTokenScanEffectRenderer())
    {
    }

    public TextRenderingLyricLine(
        ILyricTextLayouter layouter,
        ITextProgressResolver progressResolver,
        ITextHighlightEffectRenderer highlightEffectRenderer)
    {
        _layouter = layouter;
        _progressResolver = progressResolver;
        _highlightEffectRenderer = highlightEffectRenderer;
    }

    public const float TextPadding = 16;
    public const float LiftAmount = 3;

    public string? Text { get; set; }
    public override string ExpressionText => Text ?? string.Empty;
    public override bool IsTextLine => true;
    public List<LyricTextToken> Tokens { get; set; } = [];
    public string? Transliteration { get; set; }
    public string? Translation { get; set; }
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };


    protected override bool RenderCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_layout is null) return true;
        session.DrawImage(_layout.StaticPersistCache, 0, 0, _layout.SizePixelRect, 1);

        if (IsActive && context.EffectProfile is { } effectProfile)
        {
            var frame = _progressResolver.Resolve(context.CurrentLyricTime, StartTime, EndTime, _layout);
            _focusedTextRenderer.Render(
                session,
                _layout,
                frame,
                context,
                effectProfile.FocusedText,
                CurrentExpressionLine,
                CurrentExpressionFrame);
        }
        else if (IsActive && (Tokens.Count > 0 || context.Effects.SimpleLineScanning))
        {
            var frame = _progressResolver.Resolve(context.CurrentLyricTime, StartTime, EndTime, _layout);
            _highlightEffectRenderer.Render(session, _layout, frame, context);
            if (_layout.DefaultTranslationPersistCache is not null)
            {
                session.DrawImage(
                    _layout.DefaultTranslationPersistCache,
                    0,
                    _layout.TranslationRenderActualTop,
                    _layout.SizePixelRect,
                    1);
            }
        }
        else
        {
            if (_layout.DefaultTransliterationPersistCache is not null)
            {
                session.DrawImage(_layout.DefaultTransliterationPersistCache, 0, 0, _layout.SizePixelRect, 1);
            }

            session.DrawImage(_layout.DefaultTextPersistCache, 0, _layout.TextRenderActualTop, _layout.SizePixelRect, 1);
            if (_layout.DefaultTranslationPersistCache is not null)
            {
                session.DrawImage(
                    _layout.DefaultTranslationPersistCache,
                    0,
                    _layout.TranslationRenderActualTop,
                    _layout.SizePixelRect,
                    1);
            }
        }

        return true;
    }

    protected override bool TryGetStaticSourceImage(
        CanvasDrawingSession session,
        RenderContext context,
        out ICanvasImage image)
    {
        if (!IsActive && _layout is not null)
        {
            _idleLineCache ??= CreateIdleLineCache(session, _layout);
            image = _idleLineCache;
            return true;
        }

        image = null!;
        return false;
    }

    protected override void OnRenderingChanged(bool rendering)
    {
        if (rendering) return;
        _idleLineCache?.Dispose();
        _idleLineCache = null;
    }

    protected override void OnKeyFrameCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_wasActive && !IsActive) _focusedTextRenderer.ReleaseRasterCache();
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
        _focusedTextRenderer.ReleaseRasterCache();
        _idleLineCache?.Dispose();
        _idleLineCache = null;
        _cachedAlignment = TypographySelector(t => t?.Alignment, context)!.Value;
        _cachedLyricFontSize = TypographySelector(t => t?.LyricFontSize, context)!.Value;
        _cachedTransliterationFontSize = TypographySelector(t => t?.TransliterationFontSize, context)!.Value;
        _cachedTranslationFontSize = TypographySelector(t => t?.TranslationFontSize, context)!.Value;
        _cachedFontFamily = TypographySelector(t => t?.Font, context);
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
        _focusedTextRenderer.ReleaseRasterCache();
        _idleLineCache?.Dispose();
        _idleLineCache = null;
        _layout?.Dispose();
        _layout = null;
        base.Dispose();
    }
}
