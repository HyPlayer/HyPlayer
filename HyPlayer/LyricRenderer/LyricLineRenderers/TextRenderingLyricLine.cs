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

    private float _canvasWidth;
    private float _canvasHeight;
    private LyricTextLayoutSnapshot? _layout;

    private TextAlignment _cachedAlignment;
    private float _cachedLyricFontSize;
    private float _cachedTransliterationFontSize;
    private float _cachedTranslationFontSize;
    private string? _cachedFontFamily;
    private Color _cachedFocusingColor;

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
    public bool HiddenOnBlur { get; set; }
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };


    protected override bool RenderCore(CanvasDrawingSession session, RenderContext context)
    {
        if (_layout is null) return true;

        using var textCommandList = new CanvasCommandList(session);
        using (var textDrawingSession = textCommandList.CreateDrawingSession())
        {
            textDrawingSession.DrawImage(_layout.StaticPersistCache, 0, 0, _layout.SizePixelRect, 1);

            if (IsActive && (Tokens.Count > 0 || context.Effects.SimpleLineScanning))
            {
                var frame = _progressResolver.Resolve(context.CurrentLyricTime, StartTime, EndTime, _layout);
                _highlightEffectRenderer.Render(
                    textDrawingSession,
                    _layout,
                    frame,
                    context);
            }
            else
            {
                if (_layout.DefaultTransliterationPersistCache is not null)
                {
                    textDrawingSession.DrawImage(_layout.DefaultTransliterationPersistCache, 0, 0, _layout.SizePixelRect, 1);
                }

                textDrawingSession.DrawImage(_layout.DefaultTextPersistCache, 0, _layout.TextRenderActualTop, _layout.SizePixelRect, 1);
            }
        }

        session.DrawImage(textCommandList, 0, 0);

        return true;
    }

    protected override void OnKeyFrameCore(CanvasDrawingSession session, RenderContext context)
    {
        Hidden = HiddenOnBlur && !IsActive;

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

    public override void Dispose()
    {
        _layout?.Dispose();
        _layout = null;
        base.Dispose();
    }
}
