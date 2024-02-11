using System;
using Windows.Foundation;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using System.Globalization;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using System.Diagnostics;
using System.Numerics;
using Windows.UI;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

public class TextRenderingLyricLine : RenderingLyricLine
{
    public string Text { get; set; }

    public string? Translation { get; set; }
    public string? Transliteration { get; set; }

    private CanvasTextFormat textFormat;
    private CanvasTextFormat translationFormat;
    private CanvasTextFormat transliterationFormat;
    private CanvasTextLayout textLayout;

    private CanvasTextLayout? tl;
    private CanvasTextLayout? tll;
    private float _canvasWidth = 0.0f;
    private float _canvasHeight = 0.0f;

    private long _lastReactionTime = 0;
    private const long ReactionDurationTick = 5000000;
    private ReactionState _reactionState = ReactionState.Leave;

    public override void GoToReactionState(ReactionState state, long time)
    {
        _lastReactionTime = time;
        _reactionState = state;
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime,
        long renderingTick, int gap)
    {
        var drawingTop = (float)offset.Y + (HiddenOnBlur ? 10 : 30);
        if (textLayout is null)
            return true;

        // 画背景
        if (_reactionState == ReactionState.Enter)
        {
            // 为了应对居中, 获取字符 Offset
        
            double progress;
            if (renderingTick - _lastReactionTime > ReactionDurationTick)
            {
                progress = 1;
            }
            else
            {
                progress = Math.Clamp((renderingTick - _lastReactionTime) * 1.0 / ReactionDurationTick, 0 , 1);
            }
            var color = new Color()
            {
                A = (byte)(progress * 40),
                R = 0,
                G = 0,
                B = 0
            };
            session.FillRoundedRectangle((float)textLayout.LayoutBounds.Left, (float)offset.Y, (float)RenderingWidth, (float)RenderingHeight, 6, 6, color);
        }
        var actualTop = 0.0f;
        var totalCommand = new CanvasCommandList(session);
        using (var targetSession = totalCommand.CreateDrawingSession())
        {
            if (tll != null)
            {
                actualTop += HiddenOnBlur ? 10 : 0;
                targetSession.DrawTextLayout(tll, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
                actualTop += (float)tll.LayoutBounds.Height;
            }

            var textTop = actualTop;
            targetSession.DrawTextLayout(textLayout, (float)offset.X, actualTop, IdleColor);
            actualTop += (float)textLayout.LayoutBounds.Height;
            if (tl != null)
            {
                targetSession.DrawTextLayout(tl, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
            }

            if (_isFocusing)
            {
                // 做一下扫词
                var currentProgress = (currentLyricTime - StartTime) * 1.0 / (EndTime - StartTime);
                if (currentProgress < 0) return true;
                var cl = new CanvasCommandList(targetSession);
                using (CanvasDrawingSession clds = cl.CreateDrawingSession())
                {
                    clds.DrawTextLayout(textLayout, 0, 0, FocusingColor);
                }

                var accentLyric = new CropEffect
                {
                    Source = cl,
                    SourceRectangle = new Rect(textLayout.LayoutBounds.Left, textLayout.LayoutBounds.Top,
                        currentProgress * textLayout.LayoutBounds.Width, textLayout.LayoutBounds.Height),
                };
                targetSession.DrawImage(accentLyric, (float)offset.X, textTop);
            }
        }
        if (gap != 0)
        {
            _lastNoneGapTime = currentLyricTime;
            var transformEffect = new Transform2DEffect
            {
                Source = totalCommand,
                TransformMatrix = _unfocusMatrix,
            };
            session.DrawImage(transformEffect, (float)offset.X, drawingTop);
        }
        else
        {
            // 计算 Progress
            var progress = 1.0f;
            if (currentLyricTime - _lastNoneGapTime <= ScaleAnimationDuration)
            {
                progress = Math.Clamp((currentLyricTime - _lastNoneGapTime) * 1.0f / ScaleAnimationDuration, 0, 1);
            }
            var scaling = 0.9F + progress * 0.1f;
            var transformEffect = new Transform2DEffect
            {
                Source = totalCommand,
                TransformMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
                    (float)textLayout.LayoutBounds.Height / 2, scaling, scaling),
            };
            session.DrawImage(transformEffect, (float)offset.X, drawingTop);
        }

        return true;
    }

    /// <summary>
    /// 根据中心点放大
    /// </summary>
    public Matrix3x2 GetCenterMatrix(float X, float Y, float XCenter, float YCenter, float XScle, float YScle)
    {
        return Matrix3x2.CreateTranslation(-XCenter, -YCenter)
               * Matrix3x2.CreateScale(XScle, YScle)
               * Matrix3x2.CreateTranslation(X, Y)
               * Matrix3x2.CreateTranslation(XCenter, YCenter);
    }
    
    private const long ScaleAnimationDuration = 200;

    private bool _isFocusing = false;

    public bool HiddenOnBlur = false;
    private long _lastNoneGapTime;
    private float _scalingCenterX;
    private Matrix3x2 _unfocusMatrix = Matrix3x2.Identity;

    public override void OnKeyFrame(CanvasDrawingSession session, long time)
    {
        // skip
        _isFocusing = (time >= StartTime && time < EndTime);
        Hidden = false;
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }

        if (_canvasWidth == 0.0f) return;
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, double width, double height, long time)
    {
        if (HiddenOnBlur && !_isFocusing)
        {
            Hidden = true;
        }

        _canvasWidth = (float)width;
        _canvasHeight = (float)height;
        OnKeyFrame(session, time);
        OnTypographyChanged(session);
    }

    public override void OnTypographyChanged(CanvasDrawingSession session)
    {
        textFormat = new CanvasTextFormat()
        {
            FontSize = (float)(HiddenOnBlur ? LyricFontSize / 2 : LyricFontSize),
            HorizontalAlignment = TextAlignment switch
            {
                TextAlignment.Right => CanvasHorizontalAlignment.Right,
                TextAlignment.Center => CanvasHorizontalAlignment.Center,
                _ => CanvasHorizontalAlignment.Left
            },
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.Wrap,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
            FontFamily = "Microsoft YaHei UI",
            FontWeight = HiddenOnBlur ? FontWeights.Normal : FontWeights.Bold
        };
        textLayout = new CanvasTextLayout(session, Text, textFormat, _canvasWidth, _canvasHeight);
        var add = 0.0;
        if (!string.IsNullOrWhiteSpace(Transliteration) || !string.IsNullOrWhiteSpace(Translation))
        {
            if (!string.IsNullOrWhiteSpace(Transliteration))
            {
                transliterationFormat = new CanvasTextFormat()
                {
                    FontSize = (float)(HiddenOnBlur ? TransliterationFontSize / 2 : TransliterationFontSize),
                    HorizontalAlignment = TextAlignment switch
                    {
                        TextAlignment.Right => CanvasHorizontalAlignment.Right,
                        TextAlignment.Center => CanvasHorizontalAlignment.Center,
                        _ => CanvasHorizontalAlignment.Left
                    },
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.Wrap,
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    FontFamily = "Microsoft YaHei UI",
                    FontWeight = FontWeights.Normal
                };
                tll = new CanvasTextLayout(session, Transliteration, transliterationFormat, _canvasWidth,
                    _canvasHeight);
                add += 10;
            }

            if (!string.IsNullOrWhiteSpace(Translation))
            {
                translationFormat = new CanvasTextFormat()
                {
                    FontSize = (float)(HiddenOnBlur ? TranslationFontSize / 2 : TranslationFontSize),
                    HorizontalAlignment = TextAlignment switch
                    {
                        TextAlignment.Right => CanvasHorizontalAlignment.Right,
                        TextAlignment.Center => CanvasHorizontalAlignment.Center,
                        _ => CanvasHorizontalAlignment.Left
                    },
                    VerticalAlignment = CanvasVerticalAlignment.Top,
                    WordWrapping = CanvasWordWrapping.Wrap,
                    Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                    FontFamily = "Microsoft YaHei UI",
                    FontWeight = FontWeights.Normal
                };
                tl = new CanvasTextLayout(session, Translation, translationFormat, _canvasWidth, _canvasHeight);
                add += 30;
            }

            add += tll?.LayoutBounds.Height ?? 0;
            add += tl?.LayoutBounds.Height ?? 0;
        }
        _scalingCenterX = (float)(TextAlignment switch
        {
            TextAlignment.Left => 0,
            TextAlignment.Center => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width / 2,
            TextAlignment.Right => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width,
        });
        _unfocusMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
            (float)textLayout.LayoutBounds.Height / 2, 0.9F, 0.9F);
        RenderingHeight = textLayout.LayoutBounds.Height + add + (HiddenOnBlur ? 10 : 30);
        RenderingWidth = textLayout.LayoutBounds.Width;
    }
}