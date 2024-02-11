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
using Windows.UI.Xaml;

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
        private CanvasTextFormat textFormat;
        private CanvasTextFormat translationFormat;
        private CanvasTextFormat transliterationFormat;
        private CanvasTextLayout textLayout;

        private CanvasTextLayout? tl;
        private CanvasTextLayout? tll;

        private bool _isFocusing;
        private float _canvasWidth;
        private float _canvasHeight;
        public List<RenderingSyllable> Syllables { get; set; } = [];
        public string? Transliteration { get; set; }
        public string? Translation { get; set; }

        public override void GoToReactionState(ReactionState state, long time)
        {
            _lastReactionTime = time;
            _reactionState = state;
        }
        private const long ReactionDurationTick = 5000000;

        public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime, long renderingTick)
        {
            if (textLayout is null) return true;
            var actualTop = (float)offset.Y + (HiddenOnBlur ? 10 : 30);
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
                var highlightGeometry = CreateHighlightGeometry(currentLyricTime, textLayout, session);
                var textGeometry = CanvasGeometry.CreateText(textLayout);
                var highlightTextGeometry = highlightGeometry.CombineWith(textGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
                session.FillGeometry(highlightTextGeometry, (float)offset.X, textTop, FocusingColor);
            }
            return true;
        }

        private CanvasGeometry CreateHighlightGeometry(long currentTime, CanvasTextLayout textLayout,
            CanvasDrawingSession drawingSession)
        {
            var currentLyric = Syllables.Last();
            var geos = new HashSet<CanvasGeometry>();
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

                currentLyric = Syllables[index + 1];

                if (currentLyric.StartTime <= currentTime)
                {
                    // 获取当前字符的 Bound
                    var currentRegions = textLayout.GetCharacterRegions(letterPosition, currentLyric.Syllable.Length);
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
        private string _text;
        private bool _sizeChanged = true;
        private long _lastReactionTime;
        private ReactionState _reactionState = ReactionState.Leave;

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
                    tll = new CanvasTextLayout(session, Transliteration, transliterationFormat, _canvasWidth, _canvasHeight);
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
                _text = string.Join("", Syllables.Select(t => t.Syllable));
                textLayout = new CanvasTextLayout(session, _text, textFormat, _canvasWidth, _canvasHeight);
            }

            if (textLayout is null) return;
            RenderingHeight = textLayout.LayoutBounds.Height + (HiddenOnBlur ? 10 : 30) + add;
            RenderingWidth = textLayout.LayoutBounds.Width + 10;
        }
    }
}