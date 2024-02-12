#nullable enable
using HyPlayer.LyricRenderer.Abstraction.Render;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Text;
using HyPlayer.LyricRenderer.Abstraction;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Toolkit.Uwp.UI.Media;

namespace HyPlayer.LyricRenderer.LyricLineRenderers
{
    public class RenderingSyllable
    {
        public string Syllable { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
    }

    public class SyllablesRenderingLyricLine : RenderingLyricLine
    {
        public string? Text { get; set; }
        private CanvasTextFormat? textFormat;
        private CanvasTextFormat? translationFormat;
        private CanvasTextFormat? transliterationFormat;
        private CanvasTextLayout? textLayout;

        private CanvasTextLayout? tl;
        private CanvasTextLayout? tll;

        private bool _isFocusing;
        private float _canvasWidth;
        private float _canvasHeight;
        public bool IsSyllable = false;
        public List<RenderingSyllable> Syllables { get; set; } = [];
        public string? Transliteration { get; set; }
        public string? Translation { get; set; }

        public override void GoToReactionState(ReactionState state, long time)
        {
            _lastReactionTime = time;
            _reactionState = state;
        }

        private const long ReactionDurationTick = 2000000;

        private const long ScaleAnimationDuration = 200;

        public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime,
            long renderingTick, int gap)
        {
            if (textLayout is null) return true;
            var drawingTop = (float)offset.Y + (HiddenOnBlur ? 10 : 30);
            //session.DrawRectangle((float)offset.X, actualTop, (float)RenderingWidth, (float)RenderingHeight, Colors.Yellow);
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
                    progress = Math.Clamp((renderingTick - _lastReactionTime) * 1.0 / ReactionDurationTick, 0,
                        1);
                }

                var color = new Color()
                {
                    A = (byte)(progress * 40),
                    R = 0,
                    G = 0,
                    B = 0
                };
                session.FillRoundedRectangle((float)textLayout.LayoutBounds.Left, (float)offset.Y,
                    (float)RenderingWidth, (float)RenderingHeight, 6, 6, color);
            }

            var totalCommand = new CanvasCommandList(session);
            var actualTop = 0.0f;
            using (CanvasDrawingSession targetDrawingSession = totalCommand.CreateDrawingSession())
            {
                var cl = new CanvasCommandList(targetDrawingSession);
                using (CanvasDrawingSession clds = cl.CreateDrawingSession())
                {
                    clds.DrawTextLayout(textLayout, (float)offset.X, actualTop, IdleColor);

                    var textTop = actualTop;
                    if (_isFocusing)
                    {
                        var highlightGeometry = CreateHighlightGeometry(currentLyricTime, textLayout, session);
                        var textGeometry = CanvasGeometry.CreateText(textLayout);
                        var highlightTextGeometry = highlightGeometry.CombineWith(textGeometry, Matrix3x2.Identity,
                            CanvasGeometryCombine.Intersect);
                        clds.FillGeometry(highlightTextGeometry, (float)offset.X, textTop, FocusingColor);
                    }

                    actualTop += (float)textLayout.LayoutBounds.Height;
                    if (tll != null)
                    {
                        actualTop += HiddenOnBlur ? 10 : 0;
                        clds.DrawTextLayout(tll, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
                        actualTop += (float)tll.LayoutBounds.Height;
                    }

                    if (tl != null)
                    {
                        clds.DrawTextLayout(tl, (float)offset.X, actualTop, _isFocusing ? FocusingColor : IdleColor);
                    }
                }

                if (_isFocusing)
                {
                    //画发光效果
                    var opacityEffect = new Microsoft.Graphics.Canvas.Effects.OpacityEffect
                    {
                        Source = new GaussianBlurEffect
                        {
                            Source = cl,
                            BlurAmount = 4,
                        },
                        Opacity = 0.4f
                    };
                    targetDrawingSession.DrawImage(opacityEffect);
                    targetDrawingSession.DrawImage(cl);
                }
                else
                {
                    targetDrawingSession.DrawImage(cl);
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
                var blurEffect = new GaussianBlurEffect
                {
                    Source = transformEffect,
                    BlurAmount = Math.Abs(gap),
                };
                session.DrawImage(blurEffect, (float)offset.X, drawingTop);
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

        private long _lastNoneGapTime = 0;

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

        private CanvasGeometry CreateHighlightGeometry(long currentTime, CanvasTextLayout textLayout,
            CanvasDrawingSession drawingSession)
        {
            var geos = new HashSet<CanvasGeometry>();
            if (IsSyllable && Syllables is not null)
            {
                if (Syllables.Count <= 0) return CanvasGeometry.CreateGroup(drawingSession, geos.ToArray());
                var index = Syllables.FindLastIndex(t => t.EndTime <= currentTime);
                var letterPosition = Syllables.GetRange(0, index + 1).Sum(p => p.Syllable.Length);
                if (index >= 0)
                {
                    // 获取高亮的字符区域集合
                    var regions = textLayout.GetCharacterRegions(0, letterPosition);
                    foreach (var region in regions)
                    {
                        // 对每个字符创建矩形, 并加入到 geos
                        geos.Add(CanvasGeometry.CreateRectangle(drawingSession, region.LayoutBounds));
                    }
                }

                if (index <= Syllables.Count - 2)
                {
                    var currentLyric = Syllables[index + 1];

                    if (currentLyric.StartTime <= currentTime)
                    {
                        // 获取当前字符的 Bound
                        var currentRegions =
                            textLayout.GetCharacterRegions(letterPosition, currentLyric.Syllable.Length);
                        if (currentRegions is { Length: > 0 })
                        {
                            // 加个保险措施
                            // 计算当前字符的进度
                            var currentPercentage = (currentTime - currentLyric.StartTime) * 1.0 /
                                                    (currentLyric.EndTime - currentLyric.StartTime);
                            // 创建矩形
                            var lastRect = CanvasGeometry.CreateRectangle(
                                drawingSession, (float)currentRegions[0].LayoutBounds.Left,
                                (float)currentRegions[0].LayoutBounds.Top,
                                (float)(currentRegions.Sum(t => t.LayoutBounds.Width) * currentPercentage),
                                (float)currentRegions.Sum(t => t.LayoutBounds.Height));
                            geos.Add(lastRect);
                        }
                    }
                }
            }
            else
            {
                var progress = Math.Clamp((currentTime - StartTime) * 1.0 / (EndTime - StartTime), 0, 1);
                var targetWidth = progress * _theoryFlatLineWidth;
                var accumulatedWidth = 0.0;
                var i = 0;
                for (; i < _lineRectangle.Count; i++)
                {
                    if (accumulatedWidth + _lineRectangle[i].Width < targetWidth)
                    {
                        geos.Add(CanvasGeometry.CreateRectangle(drawingSession, _lineRectangle[i]));
                        accumulatedWidth += _lineRectangle[i].Width;
                    }
                    else
                        break;
                }
                // 扫描当前行
                if (_lineRectangle.Count > i)
                {
                    var currentLineRect = _lineRectangle[i];
                    var currentRect = CanvasGeometry.CreateRectangle(drawingSession, (float)currentLineRect.Left,
                        (float)currentLineRect.Top, (float)(targetWidth - accumulatedWidth),
                        (float)currentLineRect.Height);
                    geos.Add(currentRect);
                }
                    
                
            }

            // 拼合所有矩形
            return CanvasGeometry.CreateGroup(drawingSession, geos.ToArray());
        }

        public override void OnKeyFrame(CanvasDrawingSession session, long time)
        {
            // skip
            _isFocusing = (time >= StartTime && time < EndTime);
            Hidden = HiddenOnBlur && !_isFocusing;

            if (_canvasWidth == 0.0f) return;
            if (textFormat is null)
                OnTypographyChanged(session);
        }

        public bool HiddenOnBlur { get; set; }
        private string _text = "";
        private bool _sizeChanged = true;
        private long _lastReactionTime;
        private ReactionState _reactionState = ReactionState.Leave;
        private float _scalingCenterX;
        private Matrix3x2 _unfocusMatrix = Matrix3x2.Identity;

        public override void OnRenderSizeChanged(CanvasDrawingSession session, double width, double height, long time)
        {
            if (HiddenOnBlur && !_isFocusing)
            {
                Hidden = true;
            }

            _sizeChanged = true;
            _canvasWidth = (float)width;
            _canvasHeight = (float)height;
            OnKeyFrame(session, time);
            OnTypographyChanged(session);
        }

        private List<Rect> _lineRectangle = [];
        private double _theoryFlatLineWidth = 0.0;

        public override void OnTypographyChanged(CanvasDrawingSession session)
        {
            var add = 0.0;
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

            if (textLayout is null || _sizeChanged)
            {
                _sizeChanged = false;
                _text = IsSyllable ? string.Join("", Syllables.Select(t => t.Syllable)) : Text ?? "";
                textLayout = new CanvasTextLayout(session, _text, textFormat, Math.Clamp(_canvasWidth -4 ,0,int.MaxValue), _canvasHeight);

                // 创建所有行矩形
                if (!IsSyllable && Text is not null)
                {
                    _lineRectangle.Clear();
                    _theoryFlatLineWidth = 0;
                    var regions = textLayout.GetCharacterRegions(0, Text.Length);
                    if (regions is not null)
                    {
                        foreach (var canvasTextLayoutRegion in regions)
                        {
                            _lineRectangle.Add(new Rect(canvasTextLayoutRegion.LayoutBounds.Left,
                                canvasTextLayoutRegion.LayoutBounds.Top,
                                canvasTextLayoutRegion.LayoutBounds.Width,
                                canvasTextLayoutRegion.LayoutBounds.Height));
                            _theoryFlatLineWidth += canvasTextLayoutRegion.LayoutBounds.Width;
                        }

                    }
                }
            }

            if (textLayout is null) return;
            _scalingCenterX = (float)(TextAlignment switch
            {
                TextAlignment.Center => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width / 2,
                TextAlignment.Right => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width,
                TextAlignment.Left => 0,
                _ => throw new ArgumentOutOfRangeException()
            });
            _unfocusMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
                (float)textLayout.LayoutBounds.Height / 2, 0.9F, 0.9F);
            RenderingHeight = textLayout.LayoutBounds.Height + (HiddenOnBlur ? 10 : 30) + add;
            RenderingWidth = textLayout.LayoutBounds.Width + 10;
        }
    }
}