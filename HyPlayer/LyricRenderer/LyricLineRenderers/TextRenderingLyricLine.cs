using System;
using Windows.Foundation;
using Windows.UI;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using System.Globalization;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using System.Diagnostics;

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
        long renderingTick)
    {
        var actualTop = (float)offset.Y + (HiddenOnBlur ? 10 : 30);
        if (textLayout is null)
            return true;

        // 画背景
        if (_reactionState == ReactionState.Enter)
        {
            
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
            session.FillRoundedRectangle((float)offset.X, (float)offset.Y, (float)RenderingWidth, (float)RenderingHeight, 6, 6, color);
        }

        if (tll != null)
        {
            actualTop += HiddenOnBlur ? 10 : 0;
            session.DrawTextLayout(tll, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
            actualTop += (float)tll.LayoutBounds.Height;
        }

        var textTop = actualTop;
        session.DrawTextLayout(textLayout, (float)offset.X, actualTop, IdleColor);
        actualTop += (float)textLayout.LayoutBounds.Height;
        if (tl != null)
        {
            session.DrawTextLayout(tl, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
        }

        if (_isFocusing)
        {
            // 做一下扫词
            var currentProgress = (currentLyricTime - StartTime) * 1.0 / (EndTime - StartTime);
            if (currentProgress < 0) return true;
            var cl = new CanvasCommandList(session);
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
            session.DrawImage(accentLyric, (float)offset.X, textTop);
        }

        return true;
    }

    private bool _isFocusing = false;

    public bool HiddenOnBlur = false;

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

        RenderingHeight = textLayout.LayoutBounds.Height + add + (HiddenOnBlur ? 10 : 30);
        RenderingWidth = textLayout.LayoutBounds.Width + 10;
    }
}