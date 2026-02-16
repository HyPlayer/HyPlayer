#nullable enable
#nullable enable
using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.Builder;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.LyricLineRenderers
{
    public class RenderingSyllable(string syllable, long startTime, long endTime, string? transliteration)
    {
        public string Syllable { get; set; } = syllable;
        public long StartTime { get; set; } = startTime;
        public long EndTime { get; set; } = endTime;
        public long Duration { get; set; } = endTime - startTime;
        public string? Transliteration { get; set; } = transliteration;
    }

    public class SyllablesRenderingLyricLine : RenderingLyricLine
    {
        public string? Text { get; set; }
        private CanvasTextFormat? textFormat;
        private CanvasTextFormat? translationFormat;
        private CanvasTextFormat? transliterationFormat;
        private CanvasTextLayout? textLayout;

        private bool _isRomajiSyllable = false;
        private CanvasTextLayout? tl;
        private CanvasTextLayout? tll;
        public EaseFunctionBase EaseFunction { get; set; } = new CustomCircleEase { EasingMode = EasingMode.EaseOut };
        private CustomElasticEase _elasticEase = new CustomElasticEase { Springiness = 6 };

        private bool _isFocusing;
        private float _canvasWidth;
        private float _canvasHeight;
        private bool _sizeChangedWithoutNextRender = true;
        public bool IsSyllable = false;
        private int _lastSyllableIndex = -1;

        public List<RenderingSyllable> Syllables { get; set; } = [];
        public string? Transliteration { get; set; }
        public string? Translation { get; set; }

        public override void GoToReactionState(ReactionState state, RenderContext context)
        {
            _lastReactionTime = context.CurrentLyricTime;
            _reactionState = state;
        }

        private const long ReactionDurationTick = 2000000;

        private const long ScaleAnimationDuration = 500;

        private CanvasRenderTarget? _staticPersistCache;
        private CanvasRenderTarget? _defaultLyricPersistCache;
        private Rect _sizePixelRect = Rect.Empty;
        private float _lyricTextRenderActualTop = 0.0f;

        public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
        {
            if (textLayout is null) return true;

            var drawingTop = offset.Y + _drawingOffsetY;

            float actualOffsetX = offset.X - (float)textLayout.LayoutBounds.Left;
            if (_sizeChangedWithoutNextRender)
            {
                _unfocusMatrix = GetCenterMatrix(0, 0, _scalingCenterX + offset.X,
                    (float)textLayout.LayoutBounds.Height / 2, 0.8F, 0.8F);
                _sizeChangedWithoutNextRender = false;
            }

            actualOffsetX += 16; // padding left

            var totalCommand = new CanvasCommandList(session);
            var actualTop = _lyricTextRenderActualTop;
            using (CanvasDrawingSession targetDrawingSession = totalCommand.CreateDrawingSession())
            {
                
                var cl = new CanvasCommandList(targetDrawingSession);
                using (var clds = cl.CreateDrawingSession())
                {
                    var textTop = actualTop;
                    const float lift = 2;
                    var opacity = _isFocusing ? 1 : 0.3f;
                    clds.DrawImage(_staticPersistCache, 0, 0, _sizePixelRect, opacity);
                    if (_isFocusing)
                    {
                        if (IsSyllable || context.Effects.SimpleLineScanning)
                        {
                            var currentTime = context.CurrentLyricTime;
                            var currentSyllableIndex = Syllables.FindLastIndex(t =>
                                t.StartTime <= currentTime);
                            if (currentSyllableIndex != _lastSyllableIndex)
                            {
                                CreateSimpleGeometries(session, currentSyllableIndex, Syllables);
                                _lastSyllableIndex = currentSyllableIndex;
                            }

                            var matrix = Matrix3x2.CreateTranslation(0, textTop);

                            // before
                            var textLayoutCommandList = new CanvasCommandList(clds);
                            using var textLayoutSession = textLayoutCommandList.CreateDrawingSession();
                            using (textLayoutSession.CreateLayer(1, beforeCurrentSyllableGeometry, matrix))
                            {
                                textLayoutSession.DrawImage(_defaultLyricPersistCache, 0, textTop - lift, _sizePixelRect, 1);
                                //textLayoutSession.DrawTextLayout(textLayout, 0, textTop - lift,
                                //    TypographySelector(t => t?.FocusingColor, context)!.Value);
                                // Colors.Red);
                            }
                            using (textLayoutSession.CreateLayer(1, afterCurrentSyllableGeometry, matrix))
                            {
                                textLayoutSession.DrawImage(_defaultLyricPersistCache, 0, textTop, _sizePixelRect, 0.3f);
                                // Colors.Green);
                            }

                            // current
                            if (currentSyllableIndex != -1)
                            {
                                var percentage = GetCurrentSyllableHighlightPercentage(currentTime, Syllables,
                                    currentSyllableIndex);
                                var currentHighlightGeometry =
                                    CreateHighlightGeometry(session, percentage,
                                        _syllableBound.ElementAtOrDefault(currentSyllableIndex) ?? []);
                                var currentCommandList = new CanvasCommandList(clds);
                                using var currentDrawingSession = currentCommandList.CreateDrawingSession();
                                // 叠底
                                using (currentDrawingSession.CreateLayer(1, currentSyllableGeometry, matrix))
                                {
                                    currentDrawingSession.DrawImage(_defaultLyricPersistCache, 0, textTop, _sizePixelRect, 0.3f);
                                }

                                // 高亮
                                using (currentDrawingSession.CreateLayer(1, currentHighlightGeometry, matrix))
                                {
                                    currentDrawingSession.DrawImage(_defaultLyricPersistCache, 0, textTop);
                                }

                                if (Syllables[currentSyllableIndex].Duration >= 500 && false)
                                {
                                    // 绘制 Displacement Map
                                    var displacementMap = new CanvasCommandList(clds);
                                    using (var displacementSession = displacementMap.CreateDrawingSession())
                                    {
                                        displacementSession.Clear(Colors.Black);
                                        var gradientStops = new CanvasGradientStop[]
                                        {
                                            // 抬升开始点
                                            new CanvasGradientStop
                                                { Position = percentage - 0.5f, Color = Color.FromArgb(255, 0, 255, 0) },
                                            // 抬升峰值
                                            new CanvasGradientStop
                                                { Position = percentage, Color = Color.FromArgb(255, 0, 255, 0) },
                                            // 抬升结束点
                                            new CanvasGradientStop
                                                { Position = percentage + 0.5f, Color = Color.FromArgb(255, 0, 0, 0) }
                                        };
                                        using (var gradientBrush = new CanvasLinearGradientBrush(displacementSession, gradientStops))
                                        {
                                            // 创建一个从左到右的渐变
                                            gradientBrush.StartPoint = new Vector2((float)currentSyllableSize.Left, 0);
                                            gradientBrush.EndPoint = new Vector2((float)currentSyllableSize.Width + (float)currentSyllableSize.Left, 0);

                                            // 将渐变绘制到位移图上
                                            displacementSession.FillRectangle((float)currentSyllableSize.Left, 0, (float)currentSyllableSize.Width + lift, context.ViewHeight, gradientBrush);
                                            // clds.FillRectangle((float)currentSyllableSize.Left, 0, (float)currentSyllableSize.Width + lift, context.ViewHeight, gradientBrush);
                                        }
                                    }

                                    var displacementEffect = new DisplacementMapEffect
                                    {
                                        Source = currentCommandList,
                                        Displacement = displacementMap,
                                        XChannelSelect = EffectChannelSelect.Red,
                                        YChannelSelect = EffectChannelSelect.Green,
                                        Amount = lift * 2
                                    };
                                    // 整体 x 轴偏移回去, 是不是我算反了
                                    clds.DrawImage(displacementEffect, -lift, 0);
                                    //clds.DrawImage(textLayoutCommandList);
                                }
                                else
                                {
                                    var normalLift = -lift * Math.Clamp(1.0f * (context.CurrentLyricTime - Syllables[currentSyllableIndex].StartTime) / Syllables[currentSyllableIndex].Duration, 0, 1);
                                    clds.DrawImage(currentCommandList, 0, normalLift);
                                }
                                clds.DrawImage(textLayoutCommandList);
                            }
                        }
                        else
                        {
                            clds.DrawImage(_defaultLyricPersistCache, 0, textTop, _sizePixelRect, opacity);
                        }
                    }
                    else
                    {
                        clds.DrawImage(_defaultLyricPersistCache, 0, textTop, _sizePixelRect, opacity);
                    }

                    
                }

                if (_isFocusing && context.Effects.FocusHighlighting)
                {
                    var highlightEffectBuilder = new CanvasImageBuilder(cl);
                    //画发光效果
                    highlightEffectBuilder
                        .AddShadowEffect(6,
                            context.PreferTypography.ShadowColor ?? context.PreferTypography.FocusingColor!.Value)
                        .AddOpacityEffect(0.4f);
                    targetDrawingSession.DrawImage(highlightEffectBuilder.Build(), actualOffsetX, 0);
                }
                //结束时的扫词动画
                //if (context.CurrentLyricTime - EndTime >= 0 && context.CurrentLyricTime - EndTime <= ScaleAnimationDuration)
                //{
                //    using (targetDrawingSession.CreateLayer(1, highlightGeometry1))
                //    {
                //        var progress = (EaseFunction.Ease(Math.Clamp(
                //            (context.CurrentLyricTime - EndTime) * 1.0f / ScaleAnimationDuration, 0, 1)));

                //        var effectBuilder = new CanvasImageBuilder(cl);
                //        effectBuilder.AddCropEffect(new Rect(RenderingWidth * progress, 0, RenderingWidth * (1 - progress), RenderingHeight));
                //        targetDrawingSession.DrawImage(effectBuilder.Build(), actualOffsetX, 0);
                //    }
                //}
                targetDrawingSession.DrawImage(cl, actualOffsetX, 0);
            }

            var gap = _isFocusing ? 0 : Math.Clamp(Math.Abs(Id - context.CurrentLyricLineIndex), 1, 250);
            var finalEffectBuilder = new CanvasImageBuilder(totalCommand);

            if (context.Effects.ScaleWhenFocusing)
            {
                // 计算 Progress
                var progress = 0f;

                if (context.CurrentLyricTime - EndTime >= 0 &&
                    context.CurrentLyricTime - EndTime <= ScaleAnimationDuration) //缩小
                {
                    progress = 1 - ((float)EaseFunction.Ease(Math.Clamp(
                        (context.CurrentLyricTime - EndTime) * 1.0f / ScaleAnimationDuration, 0, 1)));
                }
                else if (_isFocusing && context.CurrentLyricTime - StartTime >= 0) //放大
                {
                    progress = (float)_elasticEase.Ease(Math.Clamp(
                        (context.CurrentLyricTime - StartTime) * 1.0f / 1000, 0, 1));
                }


                var scaling = 0.8F + progress * 0.2F;
                finalEffectBuilder
                    .AddTransform2DEffect(GetCenterMatrix(0, 0, _scalingCenterX + offset.X,
                        (float)textLayout.LayoutBounds.Height / 2, scaling, scaling))
                    .AddOpacityEffect(Math.Clamp(0.5f + progress * 0.5f, 0, 1));
            }
            else
            {
                if (context.Effects.ScaleWhenFocusing)
                {
                    finalEffectBuilder.AddTransform2DEffect(_unfocusMatrix);
                }
            }

            if (context.Effects.Blur && !_isFocusing && !context.IsScrolling)
            {
                finalEffectBuilder.AddGaussianBlurEffect(Math.Clamp(gap, 0, 250));
            }

            if (Common.Setting!.lyricRenderFade && !context.IsScrolling)
            {
                finalEffectBuilder.AddOpacityEffect(1 -
                                                    Math.Clamp(gap / (10f - (Common.Setting.lyricFadingRatio / 10f)), 0,
                                                        0.9f));
            }
            session.DrawImage(finalEffectBuilder.Build(), 0, drawingTop);
            _sizeChanged = false;

            // 画背景
            if (_reactionState == ReactionState.Enter && !string.IsNullOrEmpty(_text))
            {
                var color = new Color
                {
                    A = 10,
                    R = 255,
                    G = 255,
                    B = 255
                };
                session.FillRoundedRectangle(offset.X, offset.Y,
                    RenderingWidth + 2, RenderingHeight + 8, 6, 6, color);
            }

            if (context.Debug)
            {
                session.DrawText($"(X{offset.X},Y{drawingTop},W{RenderingWidth},H{RenderingHeight})", offset.X, drawingTop, Colors.Red);
                session.DrawText(Id.ToString(), offset.X, drawingTop + 15, Colors.Red);
                session.DrawRectangle(offset.X, drawingTop, RenderingWidth, RenderingHeight, Colors.Yellow);
            }


            return true;
        }
        public List<CanvasGradientStop> GetCanvasGradientStop(float percentage, float start, float end, float witdth)
        {
            float duration = end - start;
            float value = (percentage * duration + start) / witdth;

            var result = new List<CanvasGradientStop>(){
                new() { Position = Math.Clamp(value * 0.8f,0,1), Color = Color.FromArgb(255, 128, 128, 0) },
                new() { Position = Math.Clamp(value * 1.2f,0,1), Color = Color.FromArgb(255, 128, 128, 0) },

            };
            if (percentage <= 1)
            {

                result.Add(new CanvasGradientStop()
                {
                    Position = value,
                    Color = Color.FromArgb(255, 255, 255, 0)
                });
            }
            return result;
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

        private CanvasGeometry? beforeCurrentSyllableGeometry;
        private CanvasGeometry? currentSyllableGeometry;
        private Rect currentSyllableSize;
        private CanvasGeometry? afterCurrentSyllableGeometry;

        /// <summary>
        /// 获取基础矩形
        /// </summary>
        /// <param name="creator"></param>
        /// <param name="index"></param>
        /// <param name="syllables">目标歌词</param>
        /// <returns></returns>
        private void CreateSimpleGeometries(ICanvasResourceCreator creator, int index,
            List<RenderingSyllable>? syllables)
        {
            List<CanvasGeometry> beforeCurrentSyllable = [];
            List<CanvasGeometry> afterCurrentSyllable = [];
            List<CanvasGeometry> currentSyllable = [];
            if (IsSyllable && syllables is not null)
            {
                // 空行快速返回
                if (syllables.Count <= 0) return;
                // join before
                foreach (var rect in _syllableBound.Take(index))
                {
                    foreach (var rect1 in rect)
                    {
                        beforeCurrentSyllable.Add(CanvasGeometry.CreateRectangle(creator, rect1));
                    }
                }

                // join after
                foreach (var rect in _syllableBound.Skip(index + 1))
                {
                    foreach (var rect1 in rect)
                    {
                        afterCurrentSyllable.Add(CanvasGeometry.CreateRectangle(creator, rect1));
                    }
                }

                // join current
                foreach (var rect in _syllableBound.ElementAtOrDefault(index) ?? [])
                {
                    currentSyllable.Add(CanvasGeometry.CreateRectangle(creator, rect));
                }


                beforeCurrentSyllableGeometry = CanvasGeometry.CreateGroup(creator, beforeCurrentSyllable.ToArray());
                afterCurrentSyllableGeometry = CanvasGeometry.CreateGroup(creator, afterCurrentSyllable.ToArray());
                currentSyllableGeometry = CanvasGeometry.CreateGroup(creator, currentSyllable.ToArray());
                currentSyllableSize = currentSyllableGeometry.ComputeBounds();
            }
        }

        private float GetCurrentSyllableHighlightPercentage(long currentTime, List<RenderingSyllable>? syllables,
            int index)
        {
            if (syllables is null || syllables.Count <= 0) return 0;
            if (index == -1) return 0;
            var currentSyllable = syllables[index];
            var duration = currentSyllable.EndTime - currentSyllable.StartTime;
            if (duration <= 0) return 1;
            return Math.Clamp((currentTime - currentSyllable.StartTime) * 1.0f / duration, 0, 1);
        }


        private CanvasGeometry CreateHighlightGeometry(ICanvasResourceCreator creator, float percentage, Rect[] rects)
        {
            if (percentage <= 0 || rects.Length == 0) return CanvasGeometry.CreateGroup(creator, []);
            // 首先获取完整宽度
            var totalWidth = rects.Sum(t => t.Width);
            var targetWidth = totalWidth * percentage;
            var geos = new List<CanvasGeometry>();
            // 然后依次添加矩形，直到达到目标宽度
            if (rects.Length > 1)
            {
                foreach (var rect in rects)
                {
                    if (targetWidth <= 0) break;
                    if (rect.Width <= targetWidth)
                    {
                        geos.Add(CanvasGeometry.CreateRectangle(creator, rect));
                        targetWidth -= (float)rect.Width;
                    }
                    else
                    {
                        var partialRect = new Rect(rect.X, rect.Y, targetWidth, rect.Height);
                        geos.Add(CanvasGeometry.CreateRectangle(creator, partialRect));
                        targetWidth = 0;
                    }
                }
            }
            else
            {
                // 单个矩形, 直接按比例截取
                if (rects.Length == 1 && rects[0].Width > 0)
                {
                    var rect = rects[0];
                    var partialRect = new Rect(rect.X, rect.Y, rect.Width * Math.Clamp(percentage, 0, 1), rect.Height);
                    return CanvasGeometry.CreateRectangle(creator, partialRect);
                }
            }

            return CanvasGeometry.CreateGroup(creator, geos.ToArray());
        }


        public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
        {
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

        private List<Rect[]> _syllableBound = [];
        private float _drawingOffsetY;
        private bool _isInitialized = false;
        private string? _transliterationActual;

        public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
        {
            var add = 0.0f;
            var renderW = 0.0f;
            textFormat = new CanvasTextFormat
            {
                FontSize = HiddenOnBlur
                    ? TypographySelector(t => t?.LyricFontSize, context)!.Value / 2
                    : TypographySelector(t => t?.LyricFontSize, context)!.Value,
                HorizontalAlignment =
                    TypographySelector(t => t?.Alignment, context)!.Value switch
                    {
                        TextAlignment.Right => CanvasHorizontalAlignment.Right,
                        TextAlignment.Center => CanvasHorizontalAlignment.Center,
                        _ => CanvasHorizontalAlignment.Left
                    },
                VerticalAlignment = CanvasVerticalAlignment.Top,
                WordWrapping = CanvasWordWrapping.Wrap,
                Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                FontFamily = TypographySelector(t => t?.Font, context),
                FontWeight = HiddenOnBlur ? FontWeights.Normal : FontWeights.SemiBold
            };
            if (!_isInitialized)
                _isRomajiSyllable = Syllables?.Any(t => t.Transliteration is not null) ?? false;
            if (!string.IsNullOrWhiteSpace(Transliteration) || !string.IsNullOrWhiteSpace(Translation) ||
                _isRomajiSyllable)
            {
                if (!string.IsNullOrWhiteSpace(Transliteration) && context.EnableTransliteration)
                {
                    transliterationFormat = new CanvasTextFormat
                    {
                        FontSize = HiddenOnBlur
                            ? TypographySelector(t => t?.TransliterationFontSize, context)!.Value / 2
                            : TypographySelector(t => t?.TransliterationFontSize, context)!.Value,
                        HorizontalAlignment = TypographySelector(t => t?.Alignment, context)!.Value switch
                        {
                            TextAlignment.Right => CanvasHorizontalAlignment.Right,
                            TextAlignment.Center => CanvasHorizontalAlignment.Center,
                            _ => CanvasHorizontalAlignment.Left
                        },
                        VerticalAlignment = CanvasVerticalAlignment.Top,
                        WordWrapping = CanvasWordWrapping.Wrap,
                        Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                        FontFamily = TypographySelector(t => t?.Font, context),
                        FontWeight = FontWeights.Normal
                    };
                    if (!_isInitialized)
                        _transliterationActual = _isRomajiSyllable
                            ? string.Join("", Syllables!.Select(s => s.Transliteration))
                            : Transliteration;
                    tll = new CanvasTextLayout(session, _transliterationActual, transliterationFormat,
                        Math.Clamp(context.ItemWidth - 16, 0, int.MaxValue),
                        _canvasHeight);
                    add += 10;
                }
                else
                {
                    if (tll != null)
                    {
                        tll = null;
                    }

                    if (transliterationFormat != null)
                    {
                        transliterationFormat = null;
                    }
                }

                if (!string.IsNullOrWhiteSpace(Translation) && context.EnableTranslation)
                {
                    translationFormat = new CanvasTextFormat
                    {
                        FontSize = HiddenOnBlur
                            ? TypographySelector(t => t?.TranslationFontSize, context)!.Value / 2
                            : TypographySelector(t => t?.TranslationFontSize, context)!.Value,
                        HorizontalAlignment = TypographySelector(t => t?.Alignment, context)!.Value switch
                        {
                            TextAlignment.Right => CanvasHorizontalAlignment.Right,
                            TextAlignment.Center => CanvasHorizontalAlignment.Center,
                            _ => CanvasHorizontalAlignment.Left
                        },
                        VerticalAlignment = CanvasVerticalAlignment.Top,
                        WordWrapping = CanvasWordWrapping.Wrap,
                        Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
                        FontFamily = TypographySelector(t => t?.Font, context),
                        FontWeight = FontWeights.Normal
                    };
                    string? trimmedText = Translation?.ToString().TrimEnd();
                    tl = new CanvasTextLayout(session, trimmedText, translationFormat,
                        Math.Clamp(context.ItemWidth - 16, 10, int.MaxValue), _canvasHeight);
                    add += 0;
                }
                else
                {
                    if (tl != null)
                    {
                        tl = null;
                    }

                    if (translationFormat != null)
                    {
                        translationFormat = null;
                    }
                }

                add += (float)(tll?.LayoutBounds.Height ?? 0f);
                add += (float)(tl?.LayoutBounds.Height ?? 0f);
            }


            if (textLayout is null || _sizeChanged)
            {
                _sizeChanged = false;
                _text = IsSyllable ? string.Join("", Syllables!.Select(t => t.Syllable)) : Text ?? "";
                textLayout = new CanvasTextLayout(session, _text, textFormat,
                    Math.Clamp(context.ItemWidth - 16, 0, int.MaxValue), _canvasHeight);
                // 创建所有行矩形
                if (IsSyllable)
                {
                    // accumulate letter widths
                    _syllableBound.Clear();
                    var alreadyLetterCount = 0;
                    foreach (var syllable in Syllables ?? [])
                    {
                        // get region
                        var region = textLayout.GetCharacterRegions(alreadyLetterCount, syllable.Syllable.Length);
                        if (region is { Length: > 0 })
                        {
                            _syllableBound.Add(region.Select(t => t.LayoutBounds).ToArray());
                            alreadyLetterCount += syllable.Syllable.Length;
                        }
                        else
                        {
                            _syllableBound.Add([]);
                        }
                    }
                }
            }

            if (textLayout is null) return;
            _scalingCenterX = (float)(TypographySelector(t => t?.Alignment, context)!.Value switch
            {
                TextAlignment.Center => textLayout.LayoutBounds.Width / 2,
                TextAlignment.Right => textLayout.LayoutBounds.Width,
                _ => 0
            });
            _unfocusMatrix = GetCenterMatrix(0, 0, _scalingCenterX,
                (float)textLayout.LayoutBounds.Height / 2, 0.8F, 0.8F);
            _drawingOffsetY =
                (HiddenOnBlur
                    ? TypographySelector(t => t?.LyricFontSize, context)!.Value / 2
                    : TypographySelector(t => t?.LyricFontSize, context)!.Value) / 8f;
            RenderingHeight = (float)textLayout.LayoutBounds.Height + _drawingOffsetY + add;
            renderW = (float)Math.Max(textLayout.LayoutBounds.Width,
                Math.Max(tll?.LayoutBounds.Width ?? 0, tl?.LayoutBounds.Width ?? 0));
            RenderingWidth = renderW + 32;

            
            // create static persist
            _staticPersistCache?.Dispose();
            _defaultLyricPersistCache?.Dispose();
            _sizePixelRect = new Rect(0, 0, _canvasWidth, _canvasWidth);
            _staticPersistCache = new CanvasRenderTarget(session, _canvasWidth, _canvasWidth, context.Dpi);
            _defaultLyricPersistCache = new CanvasRenderTarget(session, _canvasWidth, _canvasWidth, context.Dpi);
            
            using (var pstDs = _staticPersistCache.CreateDrawingSession())
            using (var dftLyricDs = _defaultLyricPersistCache.CreateDrawingSession())
            {
                var actualTop = _drawingOffsetY;
                //罗马字
                if (tll != null)
                {
                    pstDs.DrawTextLayout(tll, 0, actualTop,
                            TypographySelector(t => t?.FocusingColor, context)!.Value);

                    actualTop += (float)tll.LayoutBounds.Height;
                }
                _lyricTextRenderActualTop = actualTop;

                dftLyricDs.DrawTextLayout(textLayout, 0, 0, TypographySelector(t => t?.FocusingColor, context)!.Value);
                actualTop += (float)textLayout.LayoutBounds.Height;

                

                //翻译
                if (tl != null)
                {
                    pstDs.DrawTextLayout(tl, 0, actualTop, TypographySelector(t => t?.FocusingColor, context)!.Value);
                }
            }


            _sizeChangedWithoutNextRender = true;
            _isInitialized = true;
        }
    }
}