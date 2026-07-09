#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.Builder;
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

    private bool _isFocusing;
    private float _canvasWidth;
    private float _canvasHeight;
    private ReactionState _reactionState = ReactionState.Leave;
    private LyricTextLayoutSnapshot? _layout;

    private TextAlignment _cachedAlignment;
    private float _cachedLyricFontSize;
    private float _cachedTransliterationFontSize;
    private float _cachedTranslationFontSize;
    private string? _cachedFontFamily;
    private Color _cachedFocusingColor;
    private Color? _cachedShadowColor;

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
    public List<LyricTextToken> Tokens { get; set; } = [];
    public string? Transliteration { get; set; }
    public string? Translation { get; set; }
    public bool HiddenOnBlur { get; set; }
    public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };

    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        _reactionState = state;
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        if (_layout is null) return true;

        var drawingTop = offset.Y + _layout.DrawingOffsetY;
        var actualOffsetX = offset.X;

        using var totalCommand = new CanvasCommandList(session);
        using (var targetDrawingSession = totalCommand.CreateDrawingSession())
        {
            using var textCommandList = new CanvasCommandList(targetDrawingSession);
            using (var textDrawingSession = textCommandList.CreateDrawingSession())
            {
                var opacity = _isFocusing ? 1 : 0.3f;
                textDrawingSession.DrawImage(_layout.StaticPersistCache, 0, 0, _layout.SizePixelRect, opacity);

                if (_isFocusing && (Tokens.Count > 0 || context.Effects.SimpleLineScanning))
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
                        textDrawingSession.DrawImage(_layout.DefaultTransliterationPersistCache, 0, 0, _layout.SizePixelRect, opacity);
                    }

                    textDrawingSession.DrawImage(_layout.DefaultTextPersistCache, 0, _layout.TextRenderActualTop, _layout.SizePixelRect, opacity);
                }
            }

            if (_isFocusing && context.Effects.FocusHighlighting)
            {
                var highlightEffectBuilder = new CanvasImageBuilder(textCommandList);
                highlightEffectBuilder
                    .AddShadowEffect(6, _cachedShadowColor ?? _cachedFocusingColor)
                    .AddOpacityEffect(0.4f);
                targetDrawingSession.DrawImage(highlightEffectBuilder.Build(), actualOffsetX, 0);
            }

            targetDrawingSession.DrawImage(textCommandList, actualOffsetX, 0);
        }

        var gap = _isFocusing ? 0 : Math.Clamp(Math.Abs(Id - context.CurrentLyricLineIndex), 1, 250);
        var finalEffectBuilder = new CanvasImageBuilder(totalCommand);

        if (context.Effects.ScaleWhenFocusing)
        {
            var progress = 0f;
            if (context.CurrentLyricTime - EndTime >= 0 &&
                context.CurrentLyricTime - EndTime <= ScaleAnimationDuration)
            {
                progress = 1 - (float)EaseFunction.Ease(Math.Clamp(
                    (context.CurrentLyricTime - EndTime) * 1.0f / ScaleAnimationDuration, 0, 1));
            }
            else if (_isFocusing && context.CurrentLyricTime - StartTime >= 0)
            {
                progress = (float)_elasticEase.Ease(Math.Clamp(
                    (context.CurrentLyricTime - StartTime) * 1.0f / 1000, 0, 1));
            }

            var scaling = 0.8F + progress * 0.2F;
            finalEffectBuilder
                .AddTransform2DEffect(GetCenterMatrix(0, 0, actualOffsetX + _layout.ScalingCenterX,
                    (float)_layout.TextLayout.LayoutBounds.Height / 2, scaling, scaling))
                .AddOpacityEffect(Math.Clamp(0.5f + progress * 0.5f, 0, 1));
        }

        if (context.Effects.Blur && !_isFocusing && !context.IsScrolling)
        {
            finalEffectBuilder.AddGaussianBlurEffect(Math.Clamp(gap, 0, 250));
        }

        var setting = Ioc.Default.GetRequiredService<Setting>();
        if (setting.lyricRenderFade && !context.IsScrolling)
        {
            finalEffectBuilder.AddOpacityEffect(1 -
                Math.Clamp(gap / (10f - (setting.lyricFadingRatio / 10f)), 0, 0.9f));
        }

        session.DrawImage(finalEffectBuilder.Build(), 0, drawingTop);
        if (_reactionState == ReactionState.Enter && !string.IsNullOrEmpty(_layout.Text))
        {
            session.FillRoundedRectangle(offset.X, offset.Y,
                RenderingWidth + 2, RenderingHeight + 8, 6, 6,
                Color.FromArgb(10, 255, 255, 255));
        }

        if (context.Debug)
        {
            session.DrawText($"(X{offset.X},Y{drawingTop},W{RenderingWidth},H{RenderingHeight})", offset.X, drawingTop, Colors.Red);
            session.DrawText(Id.ToString(), offset.X, drawingTop + 15, Colors.Red);
            session.DrawRectangle(offset.X, drawingTop, RenderingWidth, RenderingHeight, Colors.Yellow);
        }

        return true;
    }

    public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        _isFocusing = context.CurrentKeyframe >= StartTime && context.CurrentKeyframe < EndTime;
        Hidden = HiddenOnBlur && !_isFocusing;

        if (_canvasWidth == 0.0f) return;
        if (_layout is null)
            OnTypographyChanged(session, context);
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }

        _canvasWidth = context.ItemWidth;
        _canvasHeight = context.ViewHeight;
        OnKeyFrame(session, context);
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
        _cachedShadowColor = TypographySelector(t => t?.ShadowColor, context);

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
    }

    public static Matrix3x2 GetCenterMatrix(float x, float y, float xCenter, float yCenter, float xScale, float yScale)
    {
        return Matrix3x2.CreateTranslation(-xCenter, -yCenter)
               * Matrix3x2.CreateScale(xScale, yScale)
               * Matrix3x2.CreateTranslation(x, y)
               * Matrix3x2.CreateTranslation(xCenter, yCenter);
    }
}
