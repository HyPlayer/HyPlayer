#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;
using HyPlayer.Classes;
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;

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
        public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };

        private bool _isFocusing;
        private float _canvasWidth;
        private float _canvasHeight;
        public bool IsSyllable = false;
        public bool IsRomajiSyllable = false;

        public List<RenderingSyllable> Syllables { get; set; } = [];
        public List<RenderingSyllable>? RomajiSyllables { get; set; } = [];
        public string? Transliteration { get; set; }
        public string? Translation { get; set; }

        public override void GoToReactionState(ReactionState state, RenderContext context)
        {
            _lastReactionTime = context.CurrentLyricTime;
            _reactionState = state;
        }

        private const long ReactionDurationTick = 2000000;

        private const long ScaleAnimationDuration = 500;

        public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
        {
            if (textLayout is null) return true;
            
            var drawingTop = offset.Y + (HiddenOnBlur ? 10 : 30);
            // 画背景
            if (_reactionState == ReactionState.Enter)
            {
                // 为了应对居中, 获取字符 Offset

                float progress;
                if (context.RenderTick - _lastReactionTime > ReactionDurationTick)
                {
                    progress = 1;
                }
                else
                {
                    progress = Math.Clamp((context.RenderTick - _lastReactionTime) * 1.0f / ReactionDurationTick, 0,
                        1);
                }

                var color = new Color
                {
                    A = (byte)(progress * 40),
                    R = 0,
                    G = 0,
                    B = 0
                };
                session.FillRoundedRectangle((float)textLayout.LayoutBounds.Left, offset.Y,
                    RenderingWidth, RenderingHeight, 6, 6, color);
            }
            float actualX = offset.X;

            switch (context.PreferTypography.Alignment)
            {
                case TextAlignment.Left:
                    actualX += 4;
                    break;
                case TextAlignment.Right:
                    actualX -= 4;
                    break;
            }
            var totalCommand = new CanvasCommandList(session);
            var actualTop = 0.0f;
            using (CanvasDrawingSession targetDrawingSession = totalCommand.CreateDrawingSession())
            {
                var cl = new CanvasCommandList(targetDrawingSession);
                using (CanvasDrawingSession clds = cl.CreateDrawingSession())
                {
                    //罗马字
                    var idleColor = context.PreferTypography.FocusingColor;
                    idleColor.A = (byte)(idleColor.A * 0.3);
                    if (tll != null)
                    {
                        clds.DrawTextLayout(tll, actualX, actualTop, idleColor);
                        if (_isFocusing)
                        {
                            if(RomajiSyllables is not null)
                            {
                                var highlightGeometry = CreateHighlightGeometries(context.CurrentLyricTime, tll, session, RomajiSyllables,false);
                                var textGeometry = CanvasGeometry.CreateText(tll);

                                var highlightTextGeometry = highlightGeometry.geo1.CombineWith(textGeometry, Matrix3x2.Identity,
                                    CanvasGeometryCombine.Intersect);
                                if (highlightGeometry.geo2 is not null)//填充渐变矩形
                                {
                                    var color = context.PreferTypography.FocusingColor;
                                    color.A = (byte)(255 * highlightGeometry.currentPrecentage);
                                    var highlightTextGeometry2 = highlightGeometry.geo2.CombineWith(textGeometry, Matrix3x2.Identity,
                                        CanvasGeometryCombine.Intersect);
                                    clds.FillGeometry(highlightTextGeometry2, actualX, actualTop, color);
                                }

                                clds.FillGeometry(highlightTextGeometry, actualX, actualTop, context.PreferTypography.FocusingColor);
                            }

                        }
                        actualTop += (float)tll.LayoutBounds.Height;
                    }

                    //歌词
                    clds.DrawTextLayout(textLayout, actualX, actualTop, context.PreferTypography.IdleColor);
                    var textTop = actualTop;
                    if (_isFocusing)
                    {
                        var highlightGeometry = CreateHighlightGeometries(context.CurrentLyricTime, textLayout, session,Syllables);
                        var textGeometry = CanvasGeometry.CreateText(textLayout);

                        var highlightTextGeometry = highlightGeometry.geo1.CombineWith(textGeometry, Matrix3x2.Identity,
                            CanvasGeometryCombine.Intersect);
                        if (highlightGeometry.geo2 is not null)//填充渐变矩形
                        {
                            var color = context.PreferTypography.FocusingColor;
                            color.A = (byte)(128 * highlightGeometry.currentPrecentage);
                            var highlightTextGeometry2 = highlightGeometry.geo2.CombineWith(textGeometry, Matrix3x2.Identity,
                                CanvasGeometryCombine.Intersect);
                            clds.FillGeometry(highlightTextGeometry2, actualX, textTop, color);
                        }

                        clds.FillGeometry(highlightTextGeometry, actualX, textTop, context.PreferTypography.FocusingColor);

                    }
                    actualTop += (float)textLayout.LayoutBounds.Height;

                    //翻译
                    if (tl != null)
                    {
                        clds.DrawTextLayout(tl, actualX, actualTop, _isFocusing ? context.PreferTypography.FocusingColor : idleColor);
                    }
                }

                if (_isFocusing)
                {
                    //画发光效果
                    var opacityEffect = new OpacityEffect
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
            var gap = Id - context.CurrentLyricLineIndex;

            if (_isFocusing)
            {
                // 计算 Progress
                var progress = 1.0f;
                if (context.CurrentLyricTime - _lastNoneGapTime <= ScaleAnimationDuration)
                {
                    progress = (float)EaseFunction.Ease(Math.Clamp(
                        (context.CurrentLyricTime - _lastNoneGapTime) * 1.0f / ScaleAnimationDuration, 0, 1));
                }

                var scaling = 0.8F + progress * 0.2F;
                var transformEffect = new Transform2DEffect
                {
                    Source = totalCommand,
                    TransformMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
                        (float)textLayout.LayoutBounds.Height / 2, scaling, scaling),
                };
                var opacityEffect = new OpacityEffect
                {
                    Source = transformEffect,
                    Opacity = 0.5f + progress * 0.5f,
                };
                session.DrawImage(opacityEffect, actualX, drawingTop);
            }
            else
            {
                _lastNoneGapTime = context.CurrentLyricTime;
                var transformEffect = new Transform2DEffect
                {
                    Source = totalCommand,
                    TransformMatrix = _unfocusMatrix,
                };
                if (context.IsScrolling)
                {
                    session.DrawImage(transformEffect, actualX, drawingTop);
                }
                else
                {
                    var blurEffect = new GaussianBlurEffect
                    {
                        Source = transformEffect,
                        BlurAmount = Math.Clamp(Math.Abs(gap), 0, 250),
                    };
                    session.DrawImage(blurEffect, actualX, drawingTop);
                }
            }

            if (context.Debug)
            {
                session.DrawText($"({offset.X},{drawingTop})", offset.X, drawingTop, Colors.Red );
                session.DrawText(Id.ToString(), offset.X, drawingTop + 15, Colors.Red);
                session.DrawRectangle(offset.X, drawingTop, RenderingWidth, RenderingHeight, Colors.Yellow);
            }

            return true;
        }

        private long _lastNoneGapTime;

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

        /// <summary>
        /// 获取高亮矩形
        /// </summary>
        /// <param name="currentTime">当前时间</param>
        /// <param name="textLayout"></param>
        /// <param name="resourceCreator"></param>
        /// <param name="syllables">目标歌词</param>
        /// <param name="isScan">是否为扫描式（否则为渐变）</param>
        /// <returns></returns>
        private (CanvasGeometry geo1, CanvasGeometry? geo2 ,float currentPrecentage)
            CreateHighlightGeometries(long currentTime, CanvasTextLayout textLayout,
            ICanvasResourceCreator resourceCreator,List<RenderingSyllable>? syllables, bool isScan = true)
        {
            var geos = new HashSet<CanvasGeometry>();
            CanvasGeometry? geo2 = null;//渐变矩形
            var currentPercentage = 0.0f;
            if (IsSyllable && syllables is not null)
            {
                if (syllables.Count <= 0) return (CanvasGeometry.CreateGroup(resourceCreator, geos.ToArray()), geo2 , currentPercentage);
                var index = syllables.FindLastIndex(t => t.EndTime <= currentTime);
                var letterPosition = syllables.GetRange(0, index + 1).Sum(p => p.Syllable.Length);
                if (index >= 0)
                {
                    // 获取高亮的字符区域集合
                    var regions = textLayout.GetCharacterRegions(0, letterPosition);
                    foreach (var region in regions)
                    {
                        // 对每个字符创建矩形, 并加入到 geos
                        geos.Add(CanvasGeometry.CreateRectangle(resourceCreator, region.LayoutBounds));
                    }
                }

                if (index <= syllables.Count - 2)
                {
                    var currentLyric = syllables[index + 1];

                    if (currentLyric.StartTime <= currentTime)
                    {
                        // 获取当前字符的 Bound
                        var currentRegions =
                            textLayout.GetCharacterRegions(letterPosition, currentLyric.Syllable.Length);
                        if (currentRegions is { Length: > 0 })
                        {
                            // 加个保险措施
                            // 计算当前字符的进度
                            currentPercentage = (currentTime - currentLyric.StartTime) * 1.0f /
                                                    (currentLyric.EndTime - currentLyric.StartTime);
                            // 创建矩形
                            if(isScan)
                            {
                                var lastRect = CanvasGeometry.CreateRectangle(
                                    resourceCreator, (float)currentRegions[0].LayoutBounds.Left,
                                    (float)currentRegions[0].LayoutBounds.Top,
                                    (float)(currentRegions.Sum(t => t.LayoutBounds.Width) * currentPercentage),
                                    (float)currentRegions.Sum(t => t.LayoutBounds.Height));
                                geos.Add(lastRect);
                            }


                            // 高亮矩形
                            geo2 = CanvasGeometry.CreateRectangle(
                                resourceCreator, (float)currentRegions[0].LayoutBounds.Left,
                                (float)currentRegions[0].LayoutBounds.Top,
                                (float)(currentRegions.Sum(t => t.LayoutBounds.Width)),
                                (float)currentRegions.Sum(t => t.LayoutBounds.Height));
                          
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
                        geos.Add(CanvasGeometry.CreateRectangle(resourceCreator, _lineRectangle[i]));
                        accumulatedWidth += _lineRectangle[i].Width;
                    }
                    else
                        break;
                }
                // 扫描当前行
                if (_lineRectangle.Count > i)
                {
                    var currentLineRect = _lineRectangle[i];
                    var currentRect = CanvasGeometry.CreateRectangle(resourceCreator, (float)currentLineRect.Left,
                        (float)currentLineRect.Top, (float)(targetWidth - accumulatedWidth),
                        (float)currentLineRect.Height);
                    geos.Add(currentRect);
                }
                    
                
            }

            // 拼合所有矩形
            return (CanvasGeometry.CreateGroup(resourceCreator, geos.ToArray()),geo2,currentPercentage);
        }

        public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
        {
            // skip
            _isFocusing = (context.CurrentKeyframe >= StartTime) && (context.CurrentKeyframe < EndTime);
            Hidden = HiddenOnBlur && !_isFocusing;

            if (_canvasWidth == 0.0f) return;
            if (textFormat is null)
                OnTypographyChanged(session, context);
        }

        public bool HiddenOnBlur { get; set; }
        private string _text = "";
        private bool _sizeChanged = true;
        private long _lastReactionTime;
        private ReactionState _reactionState = ReactionState.Leave;
        private float _scalingCenterX;
        private Matrix3x2 _unfocusMatrix = Matrix3x2.Identity;

        public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
        {
            if (HiddenOnBlur && !_isFocusing)
            {
                Hidden = true;
            }

            _sizeChanged = true;
            _canvasWidth = context.ItemWidth;
            _canvasHeight = context.ViewHeight;
            OnKeyFrame(session, context);
            OnTypographyChanged(session, context);
        }

        private List<Rect> _lineRectangle = [];
        private float _theoryFlatLineWidth;

        public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
        {
            var add = 0.0;
            textFormat = new CanvasTextFormat
            {
                FontSize = HiddenOnBlur ? context.PreferTypography.LyricFontSize / 2 : context.PreferTypography.LyricFontSize,
                HorizontalAlignment = context.PreferTypography.Alignment switch
                {
                    TextAlignment.Right => CanvasHorizontalAlignment.Right,
                    TextAlignment.Center => CanvasHorizontalAlignment.Center,
                    _ => CanvasHorizontalAlignment.Left
                },
                VerticalAlignment = CanvasVerticalAlignment.Top,
                WordWrapping = CanvasWordWrapping.Wrap,
                Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                FontFamily = "Microsoft YaHei UI",
                FontWeight = HiddenOnBlur ? FontWeights.Normal : FontWeights.SemiBold
            };


            if (!string.IsNullOrWhiteSpace(Transliteration) || !string.IsNullOrWhiteSpace(Translation))
            {
                if (!string.IsNullOrWhiteSpace(Transliteration))
                {
                    transliterationFormat = new CanvasTextFormat
                    {
                        FontSize = HiddenOnBlur ? context.PreferTypography.TransliterationFontSize / 2 : context.PreferTypography.TransliterationFontSize,
                        HorizontalAlignment = context.PreferTypography.Alignment switch
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
                    tll = new CanvasTextLayout(session, Transliteration, transliterationFormat, Math.Clamp(_canvasWidth - 4, 0, int.MaxValue),
                        _canvasHeight);
                    add += 10;
                }

                if (!string.IsNullOrWhiteSpace(Translation))
                {
                    translationFormat = new CanvasTextFormat
                    {
                        FontSize = HiddenOnBlur ? context.PreferTypography.TranslationFontSize / 2 : context.PreferTypography.TranslationFontSize,
                        HorizontalAlignment = context.PreferTypography.Alignment switch
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
                    tl = new CanvasTextLayout(session, Translation, translationFormat, Math.Clamp(_canvasWidth - 4, 0, int.MaxValue), _canvasHeight);
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
                            _theoryFlatLineWidth += (float)canvasTextLayoutRegion.LayoutBounds.Width;
                        }

                    }
                }
            }

            if (textLayout is null) return;
            _scalingCenterX = (float)(context.PreferTypography.Alignment switch
            {
                TextAlignment.Center => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width / 2,
                TextAlignment.Right => textLayout.LayoutBounds.Left + textLayout.LayoutBounds.Width,
                TextAlignment.Left => 0,
                _ => throw new ArgumentOutOfRangeException()
            });
            _unfocusMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
                (float)textLayout.LayoutBounds.Height / 2, 0.8F, 0.8F);
            RenderingHeight = (float)(textLayout.LayoutBounds.Height + (HiddenOnBlur ? 10 : 30) + add);
            RenderingWidth = _canvasWidth - 4;
        }
    }
}