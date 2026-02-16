using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.LyricRenderer
{
    public sealed class LyricRenderView
    {
        public RenderContext Context { get; } = new();

        private const float Epsilon = 0.001f;

        public delegate void BeforeRenderDelegate(LyricRenderView view);

        public event BeforeRenderDelegate OnBeforeRender;

        public delegate void LyricLineClickedDelegate(RenderingLyricLine line);

        public event LyricLineClickedDelegate OnLyricLineClicked;

        private readonly CustomCircleEase _circleEase = new() { EasingMode = EasingMode.EaseOut };

        private bool _pointerPressed;
        private bool _jumpedLyrics = false;
        private double? _lastPointerPressedYValue;

        public bool EnableTranslation
        {
            get => Context.EnableTransliteration;
            set
            {
                Context.EnableTranslation = value;
                _isTypographyChanged = true;
                _needRecalculate = true;
            }
        }

        public bool EnableTransliteration
        {
            get => Context.EnableTransliteration;
            set
            {
                Context.EnableTransliteration = value;
                _isTypographyChanged = true;
                _needRecalculate = true;
            }
        }

        public bool HasJumpedLyrics
        {
            get => _jumpedLyrics;
        }


        private bool _isTypographyChanged = true;

        public void ChangeRenderColor(Color idleColor, Color focusingColor, Color? shadowColor = null)
        {
            Context.PreferTypography.IdleColor = idleColor;
            Context.PreferTypography.FocusingColor = focusingColor;
            Context.PreferTypography.ShadowColor = shadowColor ?? focusingColor;
            _isTypographyChanged = true;
        }

        public void ChangeRenderFontSize(float lyricSize, float translationSize, float transliterationSize)
        {
            Context.PreferTypography.LyricFontSize = lyricSize;
            Context.PreferTypography.TranslationFontSize = translationSize;
            Context.PreferTypography.TransliterationFontSize = transliterationSize;
            _isTypographyChanged = true;
        }

        public void ChangeAlignment(TextAlignment alignment)
        {
            Context.PreferTypography.Alignment = alignment;
            _isTypographyChanged = true;
            _needRecalculateSize = true;
        }

        public void ChangeBeatPerMinute(float beatPerMinute)
        {
            Context.BeatPerMinute = beatPerMinute;
            _isTypographyChanged = true;
        }

        public void ReflowTime(long time)
        {
            var keys = _keyFrameRendered.Keys.ToArray();
            foreach (var key in keys)
            {
                if (key >= time) _keyFrameRendered[key] = false;
            }

            _needRecalculate = true;
        }

        private bool _needRecalculate = false;
        private bool _needRecalculateSize = false;
        private bool _initializing = true;

        public void SetLyricLines(List<RenderingLyricLine> lines)
        {
            _initializing = true;
            Context.LyricLines.Clear();
            Context.LyricLines.AddRange(lines);
            _keyFrameRendered.Clear();
            // 将 Id 换为 Index, 方便后续读取
            for (var i = 0; i < Context.LyricLines.Count; i++)
            {
                Context.LyricLines[i].Id = i;
            }

            _keyFrameRendered.Clear();
            _targetingKeyFrames.Clear();
            Context.RenderOffsets.Clear();
            _keyFrameRendered[0] = false; // 将 0 时刻添加到 KeyFrame, 以便初始化时渲染           
            // 初始化位置
            float topleftPosition = Context.ViewHeight * Context.LyricPaddingTopRatio;

            foreach (var renderingLyricLine in Context.LyricLines)
            {
                var offset = new LineRenderOffset
                {
                    X = 4,
                    Y = topleftPosition
                };
                Context.RenderOffsets[renderingLyricLine.Id] = offset;
                Context.SnapshotRenderOffsets[renderingLyricLine.Id] = new LineRenderOffset();
                topleftPosition += renderingLyricLine.RenderingHeight + Context.LineSpacing;
                // 获取 Keyframe
                _keyFrameRendered[renderingLyricLine.StartTime] = false;
                _keyFrameRendered[renderingLyricLine.EndTime] = false;
                // 设置对照表
                if (!_targetingKeyFrames.ContainsKey(renderingLyricLine.StartTime))
                    _targetingKeyFrames[renderingLyricLine.StartTime] = new List<RenderingLyricLine>();
                if (!_targetingKeyFrames.ContainsKey(renderingLyricLine.EndTime))
                    _targetingKeyFrames[renderingLyricLine.EndTime] = new List<RenderingLyricLine>();
                _targetingKeyFrames[renderingLyricLine.StartTime].Add(renderingLyricLine);
                _targetingKeyFrames[renderingLyricLine.EndTime].Add(renderingLyricLine);
                if (renderingLyricLine.KeyFrames is not { Count: > 0 }) continue;

                // 添加自定义 KeyFrame
                foreach (var renderOptionsKey in renderingLyricLine.KeyFrames)
                {
                    if (!_targetingKeyFrames.ContainsKey(renderOptionsKey))
                        _targetingKeyFrames[renderOptionsKey] = new List<RenderingLyricLine>();
                    _targetingKeyFrames[renderOptionsKey].Add(renderingLyricLine);
                    _keyFrameRendered[renderOptionsKey] = false;
                }
            }

            _isTypographyChanged = true;
            _initializing = false;
        }

        private void RecalculateItemsSize(CanvasDrawingSession session)
        {

            Context.ItemWidth = Context.ViewWidth * Context.LyricWidthRatio;

            foreach (var renderingLyricLine in Context.LyricLines)
            {
                renderingLyricLine.OnRenderSizeChanged(session, Context);
                Context.RenderOffsets[renderingLyricLine.Id].X = CalculateRenderX(renderingLyricLine);
            }

        }

        private readonly Dictionary<long, bool> _keyFrameRendered = new();
        private readonly Dictionary<long, List<RenderingLyricLine>> _targetingKeyFrames = new();

        private void RecalculateRenderOffset(CanvasDrawingSession session)
        {
            if (Context.LyricLines is { Count: <= 0 }) return;
            Context.CurrentLyricLineIndex =
                Context.LyricLines.FindIndex(x =>
                    x.StartTime != -1 && x.StartTime <= Context.CurrentLyricTime &&
                    x.EndTime >= Context.CurrentLyricTime);
            if (Context.CurrentLyricLineIndex < 0)
                Context.CurrentLyricLineIndex =
                    Context.LyricLines.FindIndex(x => x.StartTime >= Context.CurrentLyricTime);
            if (Context.CurrentLyricLineIndex < 0) Context.CurrentLyricLineIndex = Context.LyricLines.Count - 1;
            Context.CurrentLyricLine = Context.LyricLines[Context.CurrentLyricLineIndex];
            Context.RenderingLyricLines.Clear();
            var theoryRenderAfterPosition = Context.LyricPaddingTopRatio * Context.ViewHeight + Context.ScrollingDelta;
            var theoryRenderBeforePosition = theoryRenderAfterPosition;
            var renderedAfterStartPosition = theoryRenderAfterPosition;
            var renderedBeforeStartPosition = theoryRenderAfterPosition;

            for (var i = Context.CurrentLyricLineIndex; i < Context.LyricLines.Count; i++)
            {
                var currentLine = Context.LyricLines[i];
                if (currentLine.Hidden || Context.CurrentKeyframe == 0)
                {
                    Context.RenderOffsets[currentLine.Id].Y = theoryRenderAfterPosition;
                }

                if (currentLine.Hidden)
                {
                    currentLine.Rendering = false;
                    continue;
                }

                if (renderedAfterStartPosition <= Context.ViewHeight && (Context.IsPlaying || !Context.IsScrolling) &&
                    !Context.IsSeek) // 在可视区域, 需要缓动
                    if (Context.SnapshotRenderOffsets.ContainsKey(currentLine.Id) &&
                        Math.Abs(theoryRenderAfterPosition - Context.RenderOffsets[currentLine.Id].Y) >
                        Epsilon)
                    {
                        renderedAfterStartPosition = Context.LineRollingEaseCalculator.CalculateCurrentY(
                            Context.SnapshotRenderOffsets[currentLine.Id].Y, theoryRenderAfterPosition,
                            currentLine, Context);
                        if (Context.Debug)
                        {
                            session.DrawText(renderedAfterStartPosition.ToString(), 0, renderedAfterStartPosition,
                                Colors.Green);
                        }

                        _needRecalculate = true; // 滚动中, 下一帧继续渲染
                    }

                Context.RenderOffsets[currentLine.Id].Y = renderedAfterStartPosition;
                if (renderedAfterStartPosition + currentLine.RenderingHeight + Context.LineSpacing > 0 &&
                    renderedAfterStartPosition <= Context.ViewHeight)
                {
                    Context.RenderingLyricLines.Add(currentLine);
                    currentLine.Rendering = true;
                }
                else
                {
                    currentLine.Rendering = false;
                }
                theoryRenderAfterPosition += currentLine.RenderingHeight + Context.LineSpacing;
                renderedAfterStartPosition += currentLine.RenderingHeight + Context.LineSpacing;
            }

            // 算之前的
            for (var i = Context.CurrentLyricLineIndex - 1; i >= 0; i--)
            {
                var currentLine = Context.LyricLines[i];
                if (currentLine.Hidden || Context.CurrentKeyframe == 0)
                {
                    Context.RenderOffsets[currentLine.Id].Y = renderedBeforeStartPosition;
                    renderedBeforeStartPosition -= currentLine.Hidden ? 0 : currentLine.RenderingHeight;
                    theoryRenderBeforePosition -= currentLine.Hidden ? 0 : currentLine.RenderingHeight;
                }

                if (!currentLine.Hidden && Context.CurrentKeyframe != 0)
                {
                    // 行前也要算一下
                    renderedBeforeStartPosition -= currentLine.RenderingHeight + Context.LineSpacing;
                    theoryRenderBeforePosition -= currentLine.RenderingHeight + Context.LineSpacing;
                    if (renderedBeforeStartPosition + currentLine.RenderingHeight > 0) // 可见区域, 需要判断缓动
                    {
                        if (Context.SnapshotRenderOffsets.ContainsKey(currentLine.Id) &&
                            Math.Abs(Context.RenderOffsets[currentLine.Id].Y - theoryRenderBeforePosition) >
                            Epsilon &&
                            (Context.IsPlaying ||
                             !Context.IsScrolling)
                            && !Context.IsSeek)
                        {
                            renderedBeforeStartPosition = Context.LineRollingEaseCalculator.CalculateCurrentY(
                                Context.SnapshotRenderOffsets[currentLine.Id].Y, theoryRenderBeforePosition,
                                currentLine, Context);
                            if (Context.Debug)
                            {
                                session.DrawText(renderedBeforeStartPosition.ToString(), 0, renderedBeforeStartPosition,
                                    Colors.Green);
                            }

                            _needRecalculate = true; // 滚动中, 下一帧继续渲染
                        }

                        if (renderedBeforeStartPosition + currentLine.RenderingHeight > 0 &&
                            renderedBeforeStartPosition < Context.ViewHeight)
                        {
                            currentLine.Rendering = true;
                            Context.RenderingLyricLines.Add(currentLine);
                        }
                        else
                        {
                            currentLine.Rendering = false;
                        }
                    }
                }
                else
                {
                    renderedBeforeStartPosition = theoryRenderBeforePosition;
                }


                Context.RenderOffsets[currentLine.Id].Y = renderedBeforeStartPosition;
            }
        }


        public void Draw(CanvasDrawingSession session, CanvasTimingInformation timing)
        {
            try
            {
                Context.RenderTick = timing.TotalTime.Ticks;
                if (_initializing || Context.ViewHeight == 0 || Context.ViewWidth == 0) return;
                OnBeforeRender?.Invoke(this);
                // 鼠标滚轮时间 5 s 清零
                if ((Context.ScrollingDelta != 0 || (Context.IsScrolling && !_pointerPressed)) &&
                    Context.RenderTick - _lastWheelTime > 50000000 && Context.IsPlaying || Context.IsSeek)
                {
                    // 缓动来一下吧
                    // 0.5 秒缓动到 0
                    Context.IsScrolling = false;
                    var progress = Math.Clamp((Context.RenderTick - _lastWheelTime - 50000000) / 5000000.0, 0, 1);
                    Context.ScrollingDelta = (int)(Context.ScrollingDelta * _circleEase.Ease(1 - progress));
                    if (Math.Abs(progress - 1) < Epsilon || Context.IsSeek)
                    {
                        _lastWheelTime = 0;
                        Context.ScrollingDelta = 0;
                    }

                    _needRecalculate = true;
                }

                if (_isTypographyChanged)
                {
                    _isTypographyChanged = false;
                    foreach (var renderingLyricLine in Context.LyricLines)
                    {
                        renderingLyricLine.OnTypographyChanged(session, Context);
                        Context.RenderOffsets[renderingLyricLine.Id].X = CalculateRenderX(renderingLyricLine);
                    }
                }

                foreach (var key in _keyFrameRendered.Keys.ToArray())
                {
                    if (_keyFrameRendered[key]) continue;
                    if (key >= Context.CurrentLyricTime && key != 0) continue;
                    // 该 KeyFrame 尚未渲染
                    _keyFrameRendered[key] = true;
                    //if (!_needRecalculate)
                    Context.CurrentKeyframe = key;
                    // 视图快照
                    //if (!_needRecalculate)
                    foreach (var (i, value) in Context.RenderOffsets)
                    {
                        Context.SnapshotRenderOffsets[i].Y = value.Y;
                    }


                    // 0 时刻渲染所有, 也就是初始化
                    var targets = key == 0 ? Context.LyricLines : _targetingKeyFrames[key];
                    foreach (var renderingLyricLine in targets)
                    {
                        renderingLyricLine.OnKeyFrame(session, Context);
                    }

                    _needRecalculate = true;
                }

                if (_needRecalculateSize)
                {
                    _needRecalculateSize = false;
                    RecalculateItemsSize(session);
                }

                if (_needRecalculate)
                {
                    _needRecalculate = false;
                    RecalculateRenderOffset(session);
                }

                foreach (var renderingLyricLine in Context.RenderingLyricLines)
                {
                    if (Context.RenderOffsets.GetValueOrDefault(renderingLyricLine.Id) is { } offset)
                    {
                        var doRender = renderingLyricLine.Render(session, offset, Context);
                        if (doRender == false) break;
                    }
                }

                if (Context.Debug)
                {
                    session.DrawText($"绘制时间: {timing.ElapsedTime}", 0, 0, Colors.Yellow);
                    session.DrawText($"滚动偏移: {Context.ScrollingDelta}", 0, 15, Colors.Yellow);
                    session.DrawText($"歌词时间: {Context.CurrentLyricTime}", 0, 30, Colors.Yellow);
                    session.DrawText($"绘制行数: {Context.RenderingLyricLines.Count}", 0, 45, Colors.Yellow);
                    session.DrawText($"英寸点数: {Context.Dpi}", 0, 60, Colors.Yellow);
                    // 绘制绘制边框
                    session.DrawRectangle(0, 0, Context.ViewWidth, Context.ViewHeight, Colors.Red, 5);
                }

            }
            catch
            {
                //Ignore
            }
        }

        private float CalculateRenderX(RenderingLyricLine renderingLyricLine)
        {
            switch ((renderingLyricLine.Typography?.Alignment ?? Context.PreferTypography?.Alignment))
            {
                case TextAlignment.Center:
                    return Context.RenderOffsets[renderingLyricLine.Id].X =
                        (Context.ViewWidth - renderingLyricLine.RenderingWidth) / 2;
                case TextAlignment.Right:
                    return Context.ViewWidth - renderingLyricLine.RenderingWidth;
                default:
                    return Context.RenderOffsets[renderingLyricLine.Id].X = 0;
            }
        }

        public void Redesign(float width, float height, float dpi)
        {
            Context.ViewWidth = width;
            Context.ViewHeight = height;
            Context.Dpi = dpi;
            _needRecalculateSize = true;
            _needRecalculate = true;
        }


        private long _lastWheelTime;

        public void LyricView_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
            var min = -(long)Context.LyricLines
                .Where(p => Context.LyricLines.IndexOf(p) >= Context.CurrentLyricLineIndex)
                .Sum(p => p.RenderingHeight + Context.LineSpacing);
            var max = (long)Context.LyricLines.Where(p => Context.LyricLines.IndexOf(p) < Context.CurrentLyricLineIndex)
                .Sum(p => p.RenderingHeight + Context.LineSpacing);
            Context.ScrollingDelta = Math.Clamp(Context.ScrollingDelta + delta, min, max); //限制滚动范围
            Context.IsScrolling = true;
            _lastWheelTime = Context.RenderTick;
            _needRecalculate = true;
        }


        public void LyricView_OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // 指针事件
            // 获取在指针范围的行（二分法查找）
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                var focusingLine = -1;
                int firstPosition = 0;
                int lastPosition = Context.RenderOffsets.Count - 1;
                int maximumAttempts = (int)Math.Ceiling(Math.Log(lastPosition + 1)) + 3;
                int attemptCount = 1;
                while (attemptCount <= maximumAttempts)
                {
                    attemptCount += 1;
                    int renderOffsetsKey = (firstPosition + lastPosition) / 2;
                    if (Context.RenderOffsets[renderOffsetsKey].Y <= e.GetCurrentPoint((UIElement)sender).Position.Y &&
                        Context.RenderOffsets[renderOffsetsKey].Y +
                        Context.LyricLines[renderOffsetsKey].RenderingHeight >=
                        e.GetCurrentPoint((UIElement)sender).Position.Y)
                    {
                        if (Context.PointerFocusingIndex == renderOffsetsKey) return;
                        Context.LyricLines[renderOffsetsKey].GoToReactionState(ReactionState.Enter, Context);
                        focusingLine = renderOffsetsKey;
                        break;
                    }
                    else if (lastPosition - firstPosition == 1) //结束时刻前面是下取整，两个都不是那就是没移到上面
                    {
                        if (Context.RenderOffsets[renderOffsetsKey + 1].Y <= e.GetCurrentPoint((UIElement)sender).Position.Y &&
                            Context.RenderOffsets[renderOffsetsKey + 1].Y +
                            Context.LyricLines[renderOffsetsKey + 1].RenderingHeight >=
                            e.GetCurrentPoint((UIElement)sender).Position.Y)
                        {
                            if (Context.PointerFocusingIndex == renderOffsetsKey + 1) return;
                            Context.LyricLines[renderOffsetsKey + 1].GoToReactionState(ReactionState.Enter, Context);
                            focusingLine = renderOffsetsKey + 1;
                        }

                        break;
                    }
                    else
                    {
                        if (Context.RenderOffsets[renderOffsetsKey].Y <= e.GetCurrentPoint((UIElement)sender).Position.Y)
                        {
                            firstPosition = renderOffsetsKey;
                        }
                        else lastPosition = renderOffsetsKey;
                    }
                }

                if (Context.PointerFocusingIndex != focusingLine)
                {
                    if (Context.PointerFocusingIndex != -1 && Context.LyricLines.Count > (Context.PointerFocusingIndex))
                        Context.LyricLines[Context.PointerFocusingIndex]
                            .GoToReactionState(ReactionState.Leave, Context);
                    Context.PointerFocusingIndex = focusingLine;
                }
            }
            else if (_pointerPressed == true && _lastPointerPressedYValue != null)
            {
                var yValue = e.GetCurrentPoint((UIElement)sender).Position.Y;
                var delta = (long)(yValue - _lastPointerPressedYValue);
                if (Math.Abs(delta) > 20)
                {
                    _lastPointerPressedYValue = yValue;
                    return;
                }

                var min = -(long)Context.LyricLines.Where(p => p.StartTime > Context.CurrentLyricTime)
                    .Sum(p => p.RenderingHeight);
                var max = (long)Context.LyricLines.Where(p => p.EndTime < Context.CurrentLyricTime)
                    .Sum(p => p.RenderingHeight);
                Context.ScrollingDelta = Math.Clamp(Context.ScrollingDelta + 2 * delta, min, max); //限制滚动范围
                //Debug.WriteLine(Context.ScrollingDelta);
                Context.IsScrolling = true;
                _lastWheelTime = Context.RenderTick;
                _needRecalculate = true;
                _lastPointerPressedYValue = yValue;
            }
            else if (_lastPointerPressedYValue == null)
            {
                _lastPointerPressedYValue = e.GetCurrentPoint((UIElement)sender).Position.Y;
            }
        }

        public void LyricView_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Context.PointerFocusingIndex != -1 && Context.LyricLines.Count > Context.PointerFocusingIndex)
                Context.LyricLines[Context.PointerFocusingIndex].GoToReactionState(ReactionState.Leave, Context);
            Context.PointerFocusingIndex = -1;
            _pointerPressed = false;
            _lastPointerPressedYValue = null;
        }


        public void LyricView_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _pointerPressed = true;
        }

        public void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _pointerPressed = false;
            _lastPointerPressedYValue = null;
        }

        public void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            foreach (var renderOffsetsKey in Context.RenderOffsets.Keys)
            {
                if (Context.LyricLines[renderOffsetsKey].Hidden)
                    continue;
                if (Context.RenderOffsets[renderOffsetsKey].Y <= e.GetPosition((UIElement)sender).Y &&
                    Context.RenderOffsets[renderOffsetsKey].Y + Context.LyricLines[renderOffsetsKey].RenderingHeight >=
                    e.GetPosition((UIElement)sender).Y)
                {
                    Context.LyricLines[renderOffsetsKey].GoToReactionState(ReactionState.Press, Context);
                    OnLyricLineClicked?.Invoke(Context.LyricLines[renderOffsetsKey]);
                    _jumpedLyrics = true;
                    break;
                }
            }

            Context.ScrollingDelta = 0;
            _pointerPressed = true;
        }

        private void LyricView_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _jumpedLyrics = false;
        }
    }
}