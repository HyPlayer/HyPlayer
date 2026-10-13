using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using HyPlayer.LyricRenderer.RollingCalculators;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Windows.Foundation;
using Windows.UI;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.LyricRenderer
{
    [Flags]
    public enum LyricFrameActivity
    {
        None = 0,
        LineChange = 1,
        Layout = 2,
        BlurCache = 4,
        Seek = 8
    }

    public sealed class LyricRenderView
    {
        public RenderContext Context { get; } = new();

        public LyricFrameActivity LastFrameActivity { get; private set; }
        private readonly object _frameGate = new();
        private readonly object _mutationGate = new();
        private readonly ConcurrentQueue<Action> _pendingMutations = new();
        private volatile bool _queueRenderUpdates;
        private bool _resourcesReleased;
        private int _renderThreadId;

        public void EnableRenderThreadUpdates() => _queueRenderUpdates = true;

        private void Mutate(Action action)
        {
            var onRenderThread = Volatile.Read(ref _renderThreadId) == Environment.CurrentManagedThreadId;
            var queued = false;
            lock (_mutationGate)
            {
                if (_resourcesReleased) return;
                if (_queueRenderUpdates && !onRenderThread)
                {
                    _pendingMutations.Enqueue(action);
                    queued = true;
                }
            }
            if (!queued)
            {
                lock (_frameGate)
                {
                    if (!_resourcesReleased) action();
                }
            }
            if (!onRenderThread) OnChanged?.Invoke();
        }

        // Called after the canvas stops; also serializes against a final in-flight frame.
        public void ReleaseResources()
        {
            lock (_frameGate)
            {
                lock (_mutationGate)
                {
                    if (_resourcesReleased) return;
                    _resourcesReleased = true;
                    while (_pendingMutations.TryDequeue(out var mutation)) mutation();
                    ClearCore();
                    _pendingSettings = null;
                }
            }
        }

        private const float Epsilon = 0.001f;

        public delegate void BeforeRenderDelegate(LyricRenderView view);

        public event BeforeRenderDelegate OnBeforeRender;

        public delegate void LyricLineClickedDelegate(RenderingLyricLine line);

        public event LyricLineClickedDelegate OnLyricLineClicked;

        public event Action OnChanged;

        public bool NeedsFrames
        {
            get
            {
                if (!_pendingMutations.IsEmpty) return true;
                if (_initializing || Context.ViewHeight == 0 || Context.ViewWidth == 0) return false;
                return Volatile.Read(ref _pendingSettings) is not null || _isTypographyChanged ||
                       _needRecalculateSize || _forceScrollReset || _scrollSettling || _springMoving ||
                       _staleLayoutCount > 0 || _layoutRequestPending || _needRealign ||
                       _layoutRequestSerial != _layoutSeenSerial;
            }
        }

        private readonly CustomCircleEase _circleEase = new() { EasingMode = EasingMode.EaseOut };

        private LyricRenderSettings? _pendingSettings;

        /// <summary>Queues immutable numeric settings for the next render frame.</summary>
        /// <param name="settings">The line and highlight settings snapshots.</param>
        public void SetRenderSettings(LyricRenderSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Interlocked.Exchange(ref _pendingSettings, settings);
            OnChanged?.Invoke();
        }

        private bool _pointerPressed;
        private bool _jumpedLyrics = false;
        private double? _lastPointerPressedYValue;

        public bool EnableTranslation
        {
            get => Context.EnableTranslation;
            set
            {
                Mutate(() =>
                {
                    Context.EnableTranslation = value;
                    _isTypographyChanged = true;
                    _needRecalculate = true;
                });
            }
        }

        public bool EnableTransliteration
        {
            get => Context.EnableTransliteration;
            set
            {
                Mutate(() =>
                {
                    Context.EnableTransliteration = value;
                    _isTypographyChanged = true;
                    _needRecalculate = true;
                });
            }
        }

        public bool HasJumpedLyrics
        {
            get => _jumpedLyrics;
        }


        private bool _isTypographyChanged = true;

        public void ChangeRenderColor(Color idleColor, Color focusingColor, Color? shadowColor = null)
        {
            Mutate(() => ChangeRenderColorCore(idleColor, focusingColor, shadowColor));
        }

        private void ChangeRenderColorCore(Color idleColor, Color focusingColor, Color? shadowColor = null)
        {
            Context.PreferTypography.IdleColor = idleColor;
            Context.PreferTypography.FocusingColor = focusingColor;
            Context.PreferTypography.ShadowColor = shadowColor ?? focusingColor;
            _isTypographyChanged = true;
        }

        public void ChangeRenderFontSize(
            float lyricSize,
            float translationSize,
            float transliterationSize,
            float sublineLyricSize = 0,
            float sublineTranslationSize = 0,
            float sublineTransliterationSize = 0)
        {
            Mutate(() => ChangeRenderFontSizeCore(lyricSize, translationSize, transliterationSize, sublineLyricSize, sublineTranslationSize, sublineTransliterationSize));
        }

        private void ChangeRenderFontSizeCore(
            float lyricSize,
            float translationSize,
            float transliterationSize,
            float sublineLyricSize = 0,
            float sublineTranslationSize = 0,
            float sublineTransliterationSize = 0)
        {
            if (sublineLyricSize <= 0) sublineLyricSize = lyricSize / 2;
            if (sublineTranslationSize <= 0) sublineTranslationSize = lyricSize / 2;
            if (sublineTransliterationSize <= 0) sublineTransliterationSize = lyricSize / 2;
            var sizes = new LyricFontSizes(lyricSize, translationSize, transliterationSize, sublineLyricSize,
                sublineTranslationSize, sublineTransliterationSize);
            if (sizes == (_requestedFontSizes ?? AppliedFontSizes())) return;
            _requestedFontSizes = sizes;
            if (_layoutEverApplied)
            {
                _layoutRequestSerial++;
                return;
            }

            ApplyFontSizes(sizes);
            _isTypographyChanged = true;
        }

        private void ApplyFontSizes(LyricFontSizes sizes)
        {
            Context.PreferTypography.LyricFontSize = sizes.Lyric;
            Context.PreferTypography.TranslationFontSize = sizes.Translation;
            Context.PreferTypography.TransliterationFontSize = sizes.Transliteration;
            Context.SublineTypography.LyricFontSize = sizes.SublineLyric;
            Context.SublineTypography.TranslationFontSize = sizes.SublineTranslation;
            Context.SublineTypography.TransliterationFontSize = sizes.SublineTransliteration;
        }

        private readonly record struct LyricFontSizes(float? Lyric, float? Translation, float? Transliteration,
            float? SublineLyric, float? SublineTranslation, float? SublineTransliteration);

        private LyricFontSizes AppliedFontSizes() => new(
            Context.PreferTypography.LyricFontSize,
            Context.PreferTypography.TranslationFontSize,
            Context.PreferTypography.TransliterationFontSize,
            Context.SublineTypography.LyricFontSize,
            Context.SublineTypography.TranslationFontSize,
            Context.SublineTypography.TransliterationFontSize);

        public void ChangeAlignment(TextAlignment alignment)
        {
            Mutate(() => ChangeAlignmentCore(alignment));
        }

        private void ChangeAlignmentCore(TextAlignment alignment)
        {
            Context.PreferTypography.Alignment = alignment;
            _isTypographyChanged = true;
            _needRecalculateSize = true;
        }

        public void ChangeLineSpacing(int spacing)
        {
            Mutate(() =>
            {
                if (Context.LineSpacing == spacing) return;
                Context.LineSpacing = spacing;
                _needRecalculate = true;
                _layoutMoved = true;
            });
        }

        public void ChangeBeatPerMinute(float beatPerMinute)
        {
            Mutate(() => ChangeBeatPerMinuteCore(beatPerMinute));
        }

        private void ChangeBeatPerMinuteCore(float beatPerMinute)
        {
            Context.BeatPerMinute = beatPerMinute;
            _isTypographyChanged = true;
        }

        public void ReflowTime(long time)
        {
            Mutate(() => ReflowTimeCore(time));
        }

        private void ReflowTimeCore(long time)
        {
            foreach (var key in _allKeyFrames.GetViewBetween(time, long.MaxValue))
                _pendingKeyFrames.Add(key);

            _needRecalculate = true;
        }

        private bool _needRecalculate = false;
        private bool _needRecalculateSize = false;
        private bool _initializing = true;

        public void SetLyricLines(List<RenderingLyricLine> lines)
        {
            Mutate(() => SetLyricLinesCore(lines));
        }

        private void SetLyricLinesCore(List<RenderingLyricLine> lines)
        {
            _initializing = true;
            ClearCore();
            Context.LyricLines.Clear();
            Context.LyricLines.AddRange(lines);
            _allKeyFrames.Clear();
            _pendingKeyFrames.Clear();
            _groupRoots.Clear();
            // 将 Id 换为 Index, 方便后续读取
            for (var i = 0; i < Context.LyricLines.Count; i++)
            {
                Context.LyricLines[i].RuntimeIndex = i;
            }

            _targetingKeyFrames.Clear();
            Context.RenderOffsets.Clear();
            AddKeyFrame(0); // 将 0 时刻添加到 KeyFrame, 以便初始化时渲染
            // 初始化位置
            float topleftPosition = Context.ViewHeight * Context.LyricPaddingTopRatio;

            foreach (var renderingLyricLine in Context.LyricLines)
            {
                if (_groupRoots.Count == 0 || _groupRoots[^1].GroupIndex != renderingLyricLine.GroupIndex)
                    _groupRoots.Add(renderingLyricLine);
                var offset = new LineRenderOffset
                {
                    X = 4,
                    Y = topleftPosition
                };
                Context.RenderOffsets.Add(offset);
                Context.SnapshotRenderOffsets.Add(new LineRenderOffset());
                topleftPosition += renderingLyricLine.RenderingHeight + Context.LineSpacing;
                // 获取 Keyframe
                AddKeyFrame(renderingLyricLine.StartTime);
                AddKeyFrame(renderingLyricLine.EndTime);
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
                    AddKeyFrame(renderOptionsKey);
                }
            }

            if (_staleLayouts.Length < Context.LyricLines.Count) _staleLayouts = new byte[Context.LyricLines.Count];
            Array.Clear(_staleLayouts);
            _staleLayoutCount = 0;
            _isTypographyChanged = true;
            _springSnap = true;
            _initializing = false;
        }

        public void Clear()
        {
            Mutate(() => ClearCore());
        }

        private void ClearCore()
        {
            foreach (var line in Context.LyricLines)
            {
                line.Dispose();
            }



            Context.LyricLines.Clear();
            Context.RenderingLyricLines.Clear();
            Context.RenderOffsets.Clear();
            Context.SnapshotRenderOffsets.Clear();
            _allKeyFrames.Clear();
            _pendingKeyFrames.Clear();
            _groupRoots.Clear();
            _targetingKeyFrames.Clear();
            Context.CurrentLyricLine = default!;
            Context.CurrentLyricLineIndex = 0;
            Context.PointerFocusingIndex = -1;
            Context.ScrollingDelta = 0;
            Context.IsScrolling = false;
            _scrollSettling = false;
            _springMoving = false;
            _springSnap = true;
            _needRecalculate = false;
            _needRecalculateSize = false;
            _isTypographyChanged = true;
            Array.Clear(_staleLayouts);
            _staleLayoutCount = 0;
        }

        private const byte StaleSize = 1;
        private const byte StaleTypography = 2;
        private const long LayoutQuietTicks = 120 * TimeSpan.TicksPerMillisecond;
        private const long LayoutRepeatTicks = 250 * TimeSpan.TicksPerMillisecond;

        private byte[] _staleLayouts = [];
        private int _staleLayoutCount;
        private float _requestedWidth;
        private float _requestedDpi = 96;
        private LyricFontSizes? _requestedFontSizes;
        private int _layoutRequestSerial;
        private int _layoutSeenSerial;
        private bool _layoutRequestPending;
        private bool _layoutEverApplied;
        private bool _needRealign;
        private long _layoutRequestTick;
        private long _layoutAppliedTick;

        internal double LayoutBudgetMilliseconds { get; set; } = 8;

        internal int LayoutPassCount { get; private set; }

        private void MarkLayoutsStale(byte kind)
        {
            var count = Context.LyricLines.Count;
            for (var index = 0; index < count; index++)
            {
                if (_staleLayouts[index] == 0) _staleLayoutCount++;
                if (_staleLayouts[index] < kind) _staleLayouts[index] = kind;
            }
        }

        private void UpdateLayoutRequest(bool force)
        {
            var tick = Context.RenderTick;
            if (_layoutRequestSerial != _layoutSeenSerial)
            {
                _layoutSeenSerial = _layoutRequestSerial;
                _layoutRequestTick = tick;
                _layoutRequestPending = true;
            }

            if (!_layoutRequestPending) return;
            if (!force && _layoutEverApplied && tick >= _layoutAppliedTick &&
                tick - _layoutRequestTick < LayoutQuietTicks &&
                tick - _layoutAppliedTick < LayoutRepeatTicks) return;
            _layoutRequestPending = false;
            _layoutEverApplied = true;
            _layoutAppliedTick = tick;
            var itemWidth = _requestedWidth * Context.LyricWidthRatio;
            var kind = Context.ItemWidth != itemWidth ? StaleSize : (byte)0;
            if (Context.Dpi != _requestedDpi) kind = StaleTypography;
            Context.ItemWidth = itemWidth;
            Context.Dpi = _requestedDpi;
            if (_requestedFontSizes is { } sizes && sizes != AppliedFontSizes())
            {
                ApplyFontSizes(sizes);
                kind = StaleTypography;
            }

            if (kind == 0) return;
            LayoutPassCount++;
            MarkLayoutsStale(kind);
        }

        private void ProcessStaleLayouts(CanvasDrawingSession session)
        {
            if (_staleLayoutCount <= 0) return;
            var lines = Context.LyricLines;
            var count = lines.Count;
            if (count <= 0 || _groupRoots.Count <= 0) return;
            var center = FindCurrentGroup().RuntimeIndex;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var budget = LayoutBudgetMilliseconds;
            var origin = Context.LyricPaddingTopRatio * Context.ViewHeight;
            var below = origin;
            var above = origin;
            var belowKnown = true;
            var aboveKnown = true;
            var exhausted = false;
            for (var distance = 0; distance < count && _staleLayoutCount > 0; distance++)
            {
                var index = center + distance;
                if (index < count)
                {
                    var line = lines[index];
                    var required = line.Rendering || belowKnown && below <= Context.ViewHeight;
                    if (_staleLayouts[index] != 0 && (required || !exhausted))
                    {
                        LayOutLine(session, index);
                        exhausted = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget;
                    }

                    if (_staleLayouts[index] != 0) belowKnown = false;
                    else if (!line.Hidden) below += line.RenderingHeight + Context.LineSpacing;
                }

                index = center - distance - 1;
                if (index < 0) continue;
                var previous = lines[index];
                var needed = previous.Rendering || aboveKnown && above > 0;
                if (_staleLayouts[index] != 0 && (needed || !exhausted))
                {
                    LayOutLine(session, index);
                    exhausted = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget;
                }

                if (_staleLayouts[index] != 0) aboveKnown = false;
                else if (!previous.Hidden) above -= previous.RenderingHeight + Context.LineSpacing;
            }

            _needRecalculate = true;
            _layoutMoved = true;
        }

        private void LayOutLine(CanvasDrawingSession session, int index)
        {
            var line = Context.LyricLines[index];
            var kind = _staleLayouts[index];
            _staleLayouts[index] = 0;
            _staleLayoutCount--;
            LastFrameActivity |= LyricFrameActivity.Layout;
            if (kind == StaleTypography) line.OnTypographyChanged(session, Context);
            else line.OnRenderSizeChanged(session, Context);
            Context.RenderOffsets[index].X = CalculateRenderX(line);
        }

        private readonly SortedSet<long> _allKeyFrames = [];
        private readonly SortedSet<long> _pendingKeyFrames = [];
        private readonly Dictionary<long, List<RenderingLyricLine>> _targetingKeyFrames = new();
        private readonly List<RenderingLyricLine> _groupRoots = [];

        private void AddKeyFrame(long time)
        {
            _allKeyFrames.Add(time);
            _pendingKeyFrames.Add(time);
        }

        private CanvasTransition _scrollTranslation = new CanvasTransition {
            Duration = TimeSpan.FromSeconds(0.15),
            Easing = new CustomCircleEase { EasingMode = EasingMode.EaseOut } };


        private void RecalculateRenderOffset(CanvasDrawingSession session)
        {
            if (Context.LyricLines is { Count: <= 0 }) return;
            var currentGroup = FindCurrentGroup();
            Context.CurrentLyricLineIndex = currentGroup.RuntimeIndex;
            Context.CurrentLyricLine = Context.LyricLines[Context.CurrentLyricLineIndex];
            Context.RenderingLyricLines.Clear();
            var scrollOffset = _scrollTranslation.Animate(Context.RenderTick / TimeSpan.TicksPerMillisecond, Context.ScrollingDelta);
            _scrollSettling = Math.Abs(scrollOffset - Context.ScrollingDelta) > Epsilon;
            if (_scrollSettling) _needRecalculate = true;
            var theoryRenderAfterPosition = Context.LyricPaddingTopRatio * Context.ViewHeight + scrollOffset;
            var theoryRenderBeforePosition = theoryRenderAfterPosition;
            var renderedAfterStartPosition = theoryRenderAfterPosition;
            var renderedBeforeStartPosition = theoryRenderAfterPosition;

            for (var i = Context.CurrentLyricLineIndex; i < Context.LyricLines.Count; i++)
            {
                var currentLine = Context.LyricLines[i];
                if (currentLine.Hidden || Context.CurrentKeyframe == 0)
                {
                    Context.RenderOffsets[currentLine.RuntimeIndex].Y = theoryRenderAfterPosition;
                }

                if (currentLine.Hidden)
                {
                    currentLine.Rendering = false;
                    continue;
                }

                if (renderedAfterStartPosition <= Context.ViewHeight && (Context.IsPlaying || !Context.IsScrolling) &&
                    !Context.IsSeek) // 在可视区域, 需要缓动
                    if (Math.Abs(theoryRenderAfterPosition - Context.RenderOffsets[currentLine.RuntimeIndex].Y) >
                        Epsilon)
                    {
                        renderedAfterStartPosition = Context.LineRollingEaseCalculator.CalculateCurrentY(
                            Context.SnapshotRenderOffsets[currentLine.RuntimeIndex].Y, theoryRenderAfterPosition,
                            currentLine, Context);
                        if (Context.Debug)
                        {
                            session.DrawText(renderedAfterStartPosition.ToString(), 0, renderedAfterStartPosition,
                                Colors.Green);
                        }

                        _needRecalculate = true; // 滚动中, 下一帧继续渲染
                    }

                Context.RenderOffsets[currentLine.RuntimeIndex].Y = renderedAfterStartPosition;
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
                    Context.RenderOffsets[currentLine.RuntimeIndex].Y = renderedBeforeStartPosition;
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
                        if (Math.Abs(Context.RenderOffsets[currentLine.RuntimeIndex].Y - theoryRenderBeforePosition) >
                            Epsilon &&
                            (Context.IsPlaying ||
                             !Context.IsScrolling)
                            && !Context.IsSeek)
                        {
                            renderedBeforeStartPosition = Context.LineRollingEaseCalculator.CalculateCurrentY(
                                Context.SnapshotRenderOffsets[currentLine.RuntimeIndex].Y, theoryRenderBeforePosition,
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
                    else
                    {
                        currentLine.Rendering = false;
                    }
                }
                else
                {
                    if (currentLine.Hidden) currentLine.Rendering = false;
                    renderedBeforeStartPosition = theoryRenderBeforePosition;
                }


                Context.RenderOffsets[currentLine.RuntimeIndex].Y = renderedBeforeStartPosition;
            }

            TrackOffsetVelocities();
        }

        private const double OffsetVelocityWindowMilliseconds = 100;
        private float[] _velocityOffsets = [];
        private double _velocityTime = double.NaN;

        private void TrackOffsetVelocities()
        {
            var offsets = Context.RenderOffsets;
            if (_velocityOffsets.Length < offsets.Count) _velocityOffsets = new float[offsets.Count];
            var now = Context.PreciseLyricTime;
            var elapsed = now - _velocityTime;
            var valid = elapsed > 0 && elapsed <= OffsetVelocityWindowMilliseconds && !Context.IsSeek;
            for (var i = 0; i < offsets.Count; i++)
            {
                var y = offsets[i].Y;
                offsets[i].VelocityY = valid ? (float)((y - _velocityOffsets[i]) / elapsed) : 0;
                _velocityOffsets[i] = y;
            }

            _velocityTime = now;
        }

        private RenderingLyricLine FindCurrentGroup() => FindGroupAt(Context.CurrentLyricTime);

        private RenderingLyricLine FindGroupAt(long time)
        {
            RenderingLyricLine? active = null;
            RenderingLyricLine? future = null;
            foreach (var group in _groupRoots)
            {
                if (group.GroupStartTime <= time &&
                    time < group.GroupEndTime)
                {
                    if (active is null || group.GroupStartTime < active.GroupStartTime ||
                        group.GroupStartTime == active.GroupStartTime && group.GroupIndex < active.GroupIndex)
                        active = group;
                }
                else if (group.GroupStartTime >= time &&
                         (future is null || group.GroupStartTime < future.GroupStartTime ||
                          group.GroupStartTime == future.GroupStartTime && group.GroupIndex < future.GroupIndex))
                {
                    future = group;
                }
            }

            return active ?? future ?? _groupRoots[^1];
        }

        private const int BlurCacheBuildsPerFrame = 3;
        private const double OffscreenMargin = 96;

        private enum SpringEvent { Correction, Together, LineChange, Rebase, Follow, Release }

        private readonly LineSpringField _springField = new();
        private double[] _springGoals = [];
        private double[] _springPositions = [];
        private PerLineSpringRollingCalculator? _springOwner;
        private bool _springSnap = true;
        private bool _springMoving;
        private bool _layoutMoved;
        private bool _keyframeProcessed;
        private int _springIndex;
        private double _springScroll;
        private bool _clockValid;
        private bool _lyricJumped;
        private long _clockTick;
        private double _clockLyric;
        private double _lyricRate = 1;

        private void TrackLyricClock()
        {
            var tick = Context.RenderTick;
            var lyric = Context.PreciseLyricTime;
            if (_clockValid)
            {
                var elapsedRender = (tick - _clockTick) / (double)TimeSpan.TicksPerMillisecond;
                var elapsedLyric = lyric - _clockLyric;
                if (Math.Abs(elapsedLyric) >= PerLineSpringRollingCalculator.LargeJumpMilliseconds)
                    _lyricJumped = true;
                if (Context.IsPlaying && elapsedRender is > 1 and < 250 && elapsedLyric > 0 &&
                    elapsedLyric < elapsedRender * 4.5)
                    _lyricRate += (Math.Clamp(elapsedLyric / elapsedRender, 0.25, 4) - _lyricRate) * 0.1;
            }

            _clockValid = true;
            _clockTick = tick;
            _clockLyric = lyric;
        }

        private double SecondsUntilNextLineChange(RenderingLyricLine current)
        {
            var now = Context.PreciseLyricTime;
            var probe = Context.CurrentLyricTime;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var next = long.MaxValue;
                foreach (var group in _groupRoots)
                {
                    if (group.GroupStartTime > probe && group.GroupStartTime < next) next = group.GroupStartTime;
                    if (group.GroupEndTime > probe && group.GroupEndTime < next) next = group.GroupEndTime;
                }

                if (next == long.MaxValue) return double.PositiveInfinity;
                probe = next;
                if (!ReferenceEquals(FindGroupAt(next), current)) break;
            }

            return Math.Max(0, (probe - now) / 1000 / _lyricRate);
        }

        private void RecalculateSpringOffsets(PerLineSpringRollingCalculator spring)
        {
            var lines = Context.LyricLines;
            var count = lines.Count;
            if (count <= 0) return;
            var tick = Context.RenderTick;
            var current = FindCurrentGroup();
            var index = current.RuntimeIndex;
            Context.CurrentLyricLineIndex = index;
            Context.CurrentLyricLine = lines[index];
            Context.RenderingLyricLines.Clear();
            _scrollSettling = false;

            var snap = _springSnap || _springField.Count != count || !ReferenceEquals(_springOwner, spring);
            if (snap)
            {
                _springField.Reset(count);
                if (_springGoals.Length < count)
                {
                    _springGoals = new double[count];
                    _springPositions = new double[count];
                }

                _springOwner = spring;
                _springSnap = false;
            }

            var goals = _springGoals;
            var positions = _springPositions;
            var viewHeight = (double)Context.ViewHeight;
            var spacing = Context.LineSpacing;
            var scroll = (double)Context.ScrollingDelta;
            var origin = Context.LyricPaddingTopRatio * viewHeight + scroll;
            var cursor = origin;
            for (var i = index; i < count; i++)
            {
                goals[i] = cursor;
                if (!lines[i].Hidden) cursor += lines[i].RenderingHeight + spacing;
            }

            cursor = origin;
            for (var i = index - 1; i >= 0; i--)
            {
                if (!lines[i].Hidden) cursor -= lines[i].RenderingHeight + spacing;
                goals[i] = cursor;
            }

            var first = -1;
            var visible = 0;
            for (var i = 0; i < count; i++)
            {
                var line = lines[i];
                line.RestDistance = viewHeight > 0 ? (float)(Math.Abs(goals[i] - origin) / viewHeight) : 0;
                var position = positions[i] = _springField.Advance(i, tick, out _);
                if (line.Hidden || position + line.RenderingHeight <= 0 || position >= viewHeight) continue;
                if (first < 0) first = i;
                visible++;
            }

            var scrollChanged = scroll != _springScroll;
            var mode = Context.IsSeek || _lyricJumped ||
                       Math.Abs(index - _springIndex) >= PerLineSpringRollingCalculator.LargeJumpLines
                ? SpringEvent.Together
                : index != _springIndex || _keyframeProcessed
                    ? SpringEvent.LineChange
                    : _layoutMoved
                        ? SpringEvent.Rebase
                        : !scrollChanged
                            ? SpringEvent.Correction
                            : scroll == 0 && !Context.IsScrolling
                                ? SpringEvent.Release
                                : SpringEvent.Follow;
            double response = PerLineSpringRollingCalculator.TogetherResponseSeconds;
            double damping = 1;
            double step = 0;
            if (mode == SpringEvent.LineChange)
            {
                var gap = SecondsUntilNextLineChange(current);
                PerLineSpringRollingCalculator.LineChangeSpring(gap, out response, out damping);
                step = PerLineSpringRollingCalculator.CascadeStep(gap, response, visible);
            }
            else if (mode == SpringEvent.Follow)
            {
                response = PerLineSpringRollingCalculator.FollowResponseSeconds;
            }
            else if (mode == SpringEvent.Release)
            {
                response = PerLineSpringRollingCalculator.ReleaseResponseSeconds;
                damping = PerLineSpringRollingCalculator.ReleaseDamping;
            }

            var lastRank = visible - 1 + PerLineSpringRollingCalculator.CascadeTrailingLines;
            var rank = -1;
            for (var i = 0; i < count; i++)
            {
                var line = lines[i];
                var goal = goals[i];
                if (snap || line.Hidden)
                {
                    _springField.Snap(i, goal);
                    continue;
                }

                if (first >= 0 && i >= first && rank < lastRank) rank++;
                var delta = goal - _springField.Goal(i);
                if (Math.Abs(delta) <= Epsilon) continue;
                if (mode == SpringEvent.Rebase)
                {
                    _springField.Shift(i, delta);
                    continue;
                }

                var position = positions[i];
                if (mode == SpringEvent.Together && viewHeight > 0 && Math.Abs(delta) > viewHeight)
                {
                    var excess = delta - Math.Sign(delta) * viewHeight;
                    _springField.Shift(i, excess);
                    position += excess;
                    delta -= excess;
                }

                var height = line.RenderingHeight;
                if (position + height < -OffscreenMargin && goal + height < -OffscreenMargin ||
                    position > viewHeight + OffscreenMargin && goal > viewHeight + OffscreenMargin)
                {
                    _springField.Snap(i, goal);
                    continue;
                }

                if (Math.Abs(delta) < PerLineSpringRollingCalculator.CorrectionDistance)
                {
                    _springField.Retarget(i, goal, tick, 0,
                        PerLineSpringRollingCalculator.CorrectionResponseSeconds, 1);
                    continue;
                }

                var delay = mode == SpringEvent.LineChange
                    ? (long)(Math.Max(rank, 0) * step * TimeSpan.TicksPerSecond)
                    : 0;
                _springField.Retarget(i, goal, tick, delay, response, damping);
            }

            var moving = false;
            for (var i = index; i < count; i++) moving |= PlaceSpringLine(i, tick);
            for (var i = index - 1; i >= 0; i--) moving |= PlaceSpringLine(i, tick);
            _springIndex = index;
            _springScroll = scroll;
            _layoutMoved = false;
            _lyricJumped = false;
            _springMoving = moving;
            if (moving) _needRecalculate = true;
        }

        private bool PlaceSpringLine(int index, long tick)
        {
            var line = Context.LyricLines[index];
            var position = (float)_springField.Advance(index, tick, out _);
            Context.RenderOffsets[index].Y = position;
            var rendering = !line.Hidden && position + line.RenderingHeight + Context.LineSpacing > 0 &&
                            position <= Context.ViewHeight;
            line.Rendering = rendering;
            if (rendering) Context.RenderingLyricLines.Add(line);
            return _springField.IsMoving(index);
        }


        public void Draw(CanvasDrawingSession session, CanvasTimingInformation timing)
        {
            lock (_frameGate)
            {
                if (_resourcesReleased) return;
                Volatile.Write(ref _renderThreadId, Environment.CurrentManagedThreadId);
                try
                {
                    while (_pendingMutations.TryDequeue(out var mutation)) mutation();
                    DrawCore(session, timing);
                }
                finally
                {
                    Volatile.Write(ref _renderThreadId, 0);
                }
            }
            RaiseScrollPreviewChanged();
        }

        private void DrawCore(CanvasDrawingSession session, CanvasTimingInformation timing)
        {
            try
            {
                Context.RenderTick = timing.TotalTime.Ticks;
                LastFrameActivity = LyricFrameActivity.None;
                if (_initializing || Context.ViewHeight == 0 || Context.ViewWidth == 0) return;
                OnBeforeRender?.Invoke(this);
                if (Context.IsSeek) LastFrameActivity |= LyricFrameActivity.Seek;
                var spring = Context.LineRollingEaseCalculator as PerLineSpringRollingCalculator;
                Context.PerLineMotion = spring is not null;
                Context.BlurCacheBudget = BlurCacheBuildsPerFrame;
                if (spring is not null) TrackLyricClock();
                if (Interlocked.Exchange(ref _pendingSettings, null) is { } pendingSettings)
                {
                    var inferChanged = Context.Settings.Focused.InferWords != pendingSettings.Focused.InferWords;
                    Context.Settings = pendingSettings;
                    // Only token inference changes the layout; numeric changes retain GPU resources.
                    _isTypographyChanged |= inferChanged;
                    _needRecalculate |= inferChanged;
                }
                // 鼠标滚轮时间 5 s 清零
                if ((Context.ScrollingDelta != 0 || (Context.IsScrolling && !_pointerPressed)) &&
                    Context.RenderTick - _lastWheelTime > 50000000 && (Context.IsPlaying || _forceScrollReset) || Context.IsSeek)
                {
                    // 缓动来一下吧
                    // 0.5 秒缓动到 0
                    Context.IsScrolling = false;
                    var progress = spring is not null
                        ? 1
                        : Math.Clamp((Context.RenderTick - _lastWheelTime - 50000000) / 5000000.0, 0, 1);
                    Context.ScrollingDelta = (int)(Context.ScrollingDelta * _circleEase.Ease(1 - progress));
                    if (Math.Abs(progress - 1) < Epsilon || Context.IsSeek || Context.ScrollingDelta == 0)
                    {
                        _lastWheelTime = 0;
                        Context.ScrollingDelta = 0;
                        _forceScrollReset = false;
                        _scrollPreviewing = false;
                    }

                    _needRecalculate = true;
                }

                if (_scrollPreviewing && Context.ScrollingDelta == 0 && !Context.IsScrolling)
                {
                    _forceScrollReset = false;
                    _scrollPreviewing = false;
                }

                var typographyChanged = _isTypographyChanged;
                UpdateLayoutRequest(typographyChanged);
                if (typographyChanged)
                {
                    _isTypographyChanged = false;
                    MarkLayoutsStale(StaleTypography);
                }

                if (_needRecalculateSize)
                {
                    _needRecalculateSize = false;
                    MarkLayoutsStale(StaleSize);
                }

                ProcessStaleLayouts(session);
                if (_needRealign)
                {
                    _needRealign = false;
                    foreach (var renderingLyricLine in Context.LyricLines)
                        Context.RenderOffsets[renderingLyricLine.RuntimeIndex].X = CalculateRenderX(renderingLyricLine);
                }

                while (_pendingKeyFrames.Count > 0)
                {
                    var key = _pendingKeyFrames.Min;
                    if (key > Context.CurrentLyricTime && key != 0) break;
                    _pendingKeyFrames.Remove(key);
                    // 该 KeyFrame 尚未渲染
                    //if (!_needRecalculate)
                    Context.CurrentKeyframe = key;
                    // 视图快照
                    //if (!_needRecalculate)
                    var sinceVelocity = Context.PreciseLyricTime - _velocityTime;
                    var moving = sinceVelocity >= 0 && sinceVelocity <= OffsetVelocityWindowMilliseconds && !Context.IsSeek;
                    Context.SnapshotLyricTime = moving ? _velocityTime : key;
                    for (var i = 0; i < Context.RenderOffsets.Count; i++)
                    {
                        Context.SnapshotRenderOffsets[i].Y = Context.RenderOffsets[i].Y;
                        Context.SnapshotRenderOffsets[i].VelocityY = moving ? Context.RenderOffsets[i].VelocityY : 0;
                    }


                    // 0 时刻渲染所有, 也就是初始化
                    var targets = key == 0 ? Context.LyricLines : _targetingKeyFrames[key];
                    foreach (var renderingLyricLine in targets)
                    {
                        renderingLyricLine.OnKeyFrame(session, Context);
                    }

                    _needRecalculate = true;
                    _keyframeProcessed = true;
                    LastFrameActivity |= LyricFrameActivity.LineChange;
                }

                if (_needRecalculate)
                {
                    _needRecalculate = false;
                    if (spring is null) RecalculateRenderOffset(session);
                    else RecalculateSpringOffsets(spring);
                    _keyframeProcessed = false;
                }

                foreach (var renderingLyricLine in Context.RenderingLyricLines)
                {
                    var offset = Context.RenderOffsets[renderingLyricLine.RuntimeIndex];
                    var doRender = renderingLyricLine.Render(session, offset, Context);
                    if (doRender == false) break;
                }

                if (Context.BlurCacheBudget < BlurCacheBuildsPerFrame) LastFrameActivity |= LyricFrameActivity.BlurCache;

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
                    return Context.RenderOffsets[renderingLyricLine.RuntimeIndex].X =
                        (Context.ViewWidth - renderingLyricLine.RenderingWidth) / 2;
                case TextAlignment.Right:
                    return Context.ViewWidth - renderingLyricLine.RenderingWidth;
                default:
                    return Context.RenderOffsets[renderingLyricLine.RuntimeIndex].X = 0;
            }
        }

        public void Redesign(float width, float height, float dpi)
        {
            Mutate(() => RedesignCore(width, height, dpi));
        }

        private void RedesignCore(float width, float height, float dpi)
        {
            if (Context.ViewHeight != height)
            {
                Context.ViewHeight = height;
                _needRecalculate = true;
                _layoutMoved = true;
            }

            if (Context.ViewWidth != width)
            {
                Context.ViewWidth = width;
                _needRealign = true;
            }

            if (_requestedWidth == width && _requestedDpi == dpi &&
                Context.ItemWidth == width * Context.LyricWidthRatio && Context.Dpi == dpi) return;
            _requestedWidth = width;
            _requestedDpi = dpi;
            _layoutRequestSerial++;
        }


        private long _lastWheelTime;
        private bool _forceScrollReset;
        private bool _scrollSettling;
        private volatile bool _scrollPreviewing;
        private int _scrollPreviewNotified;

        public event Action ScrollPreviewChanged;

        public bool IsScrollPreviewing => _scrollPreviewing;

        public float RenderOriginX { get; set; }

        public float RenderOriginY { get; set; }

        private void RaiseScrollPreviewChanged()
        {
            var previewing = _scrollPreviewing ? 1 : 0;
            if (Interlocked.Exchange(ref _scrollPreviewNotified, previewing) != previewing)
                ScrollPreviewChanged?.Invoke();
        }

        public void ResetScrolling()
        {
            lock (_frameGate)
            {
                if (_resourcesReleased || !_scrollPreviewing) return;
                _pointerPressed = false;
                _lastPointerPressedYValue = null;
                _lastWheelTime = Context.RenderTick - 50000000;
                _forceScrollReset = true;
                _needRecalculate = true;
            }
            OnChanged?.Invoke();
        }

        public void CancelScrolling()
        {
            if (!_scrollPreviewing) return;
            lock (_frameGate)
            {
                if (_resourcesReleased) return;
                Context.ScrollingDelta = 0;
                Context.IsScrolling = false;
                _lastWheelTime = 0;
                _forceScrollReset = false;
                _scrollPreviewing = false;
                _needRecalculate = true;
            }
            RaiseScrollPreviewChanged();
        }

        private bool IsPointInLine(int index, Point point)
        {
            if ((uint)index >= (uint)Context.RenderOffsets.Count || index >= Context.LyricLines.Count) return false;
            var offset = Context.RenderOffsets[index];
            var line = Context.LyricLines[index];
            var x = point.X - RenderOriginX;
            var y = point.Y - RenderOriginY;
            return y >= offset.Y && y <= offset.Y + line.RenderingHeight &&
                   x >= offset.X && x <= offset.X + line.RenderingWidth;
        }

        internal int FindLineAt(Point point)
        {
            var y = point.Y - RenderOriginY;
            var low = 0;
            var high = Math.Min(Context.RenderOffsets.Count, Context.LyricLines.Count) - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var top = Context.RenderOffsets[mid].Y;
                if (y < top) high = mid - 1;
                else if (y > top + Context.LyricLines[mid].RenderingHeight) low = mid + 1;
                else return IsPointInLine(mid, point) ? mid : -1;
            }

            return -1;
        }

        public void LyricView_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            ScrollBy(e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta);
        }

        internal void ScrollBy(int delta)
        {
            lock (_frameGate)
            {
                if (!_resourcesReleased) ScrollByCore(delta);
            }
            RaiseScrollPreviewChanged();
            OnChanged?.Invoke();
        }

        private void ScrollByCore(int delta)
        {
            double before = 0;
            double after = 0;
            for (var index = 0; index < Context.LyricLines.Count; index++)
            {
                var height = Context.LyricLines[index].RenderingHeight + Context.LineSpacing;
                if (index < Context.CurrentLyricLineIndex) before += height;
                else after += height;
            }
            var min = -(long)after;
            var max = (long)before;
            Context.ScrollingDelta = Math.Clamp(Context.ScrollingDelta + delta, min, max); //限制滚动范围
            Context.IsScrolling = true;
            _lastWheelTime = Context.RenderTick;
            _forceScrollReset = false;
            _needRecalculate = true;
            _scrollPreviewing = true;
        }


        public void LyricView_OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            lock (_frameGate)
            {
                if (!_resourcesReleased) LyricView_OnPointerMovedCore(sender, e);
            }
            RaiseScrollPreviewChanged();
            OnChanged?.Invoke();
        }

        private void LyricView_OnPointerMovedCore(object sender, PointerRoutedEventArgs e)
        {
            // 指针事件
            // 获取在指针范围的行（二分法查找）
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
            {
                var focusingLine = FindLineAt(e.GetCurrentPoint((UIElement)sender).Position);
                if (Context.PointerFocusingIndex == focusingLine) return;
                if (Context.PointerFocusingIndex != -1 && Context.LyricLines.Count > Context.PointerFocusingIndex)
                    Context.LyricLines[Context.PointerFocusingIndex].GoToReactionState(ReactionState.Leave, Context);
                if (focusingLine != -1)
                    Context.LyricLines[focusingLine].GoToReactionState(ReactionState.Enter, Context);
                Context.PointerFocusingIndex = focusingLine;
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
                _forceScrollReset = false;
                _needRecalculate = true;
                _lastPointerPressedYValue = yValue;
                _scrollPreviewing = true;
            }
            else if (_lastPointerPressedYValue == null)
            {
                _lastPointerPressedYValue = e.GetCurrentPoint((UIElement)sender).Position.Y;
            }
        }

        public void LyricView_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            lock (_frameGate)
            {
                if (!_resourcesReleased) LyricView_OnPointerExitedCore(sender, e);
            }
            OnChanged?.Invoke();
        }

        private void LyricView_OnPointerExitedCore(object sender, PointerRoutedEventArgs e)
        {
            if (Context.PointerFocusingIndex != -1 && Context.LyricLines.Count > Context.PointerFocusingIndex)
                Context.LyricLines[Context.PointerFocusingIndex].GoToReactionState(ReactionState.Leave, Context);
            Context.PointerFocusingIndex = -1;
            _pointerPressed = false;
            _lastPointerPressedYValue = null;
        }


        public void LyricView_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            lock (_frameGate)
            {
                if (!_resourcesReleased) LyricView_OnPointerPressedCore(sender, e);
            }
        }

        private void LyricView_OnPointerPressedCore(object sender, PointerRoutedEventArgs e)
        {
            _pointerPressed = true;
        }

        public void LyricView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            lock (_frameGate)
            {
                if (!_resourcesReleased) LyricView_PointerReleasedCore(sender, e);
            }
        }

        private void LyricView_PointerReleasedCore(object sender, PointerRoutedEventArgs e)
        {
            _pointerPressed = false;
            _lastPointerPressedYValue = null;
        }

        public void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            RenderingLyricLine? clicked;
            lock (_frameGate)
            {
                if (_resourcesReleased) return;
                clicked = OnDoubleTappedCore(sender, e);
            }
            RaiseScrollPreviewChanged();
            OnChanged?.Invoke();
            // Navigation can unload the canvas and join its game loop. Do not
            // hold the frame gate while invoking application callbacks.
            if (clicked is not null) OnLyricLineClicked?.Invoke(clicked);
        }

        private RenderingLyricLine? OnDoubleTappedCore(object sender, DoubleTappedRoutedEventArgs e)
        {
            RenderingLyricLine? clicked = null;
            var position = e.GetPosition((UIElement)sender);
            for (var renderOffsetsKey = 0; renderOffsetsKey < Context.RenderOffsets.Count; renderOffsetsKey++)
            {
                if (Context.LyricLines[renderOffsetsKey].Hidden)
                    continue;
                if (IsPointInLine(renderOffsetsKey, position))
                {
                    Context.LyricLines[renderOffsetsKey].GoToReactionState(ReactionState.Press, Context);
                    clicked = Context.LyricLines[renderOffsetsKey];
                    _jumpedLyrics = true;
                    break;
                }
            }

            Context.ScrollingDelta = 0;
            _pointerPressed = true;
            _scrollPreviewing = false;
            return clicked;
        }

        private void LyricView_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _jumpedLyrics = false;
        }
    }
}
