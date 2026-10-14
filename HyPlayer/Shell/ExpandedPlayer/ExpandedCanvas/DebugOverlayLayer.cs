using System;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml.Input;
using HyPlayer.LyricRenderer;
using HyPlayer.UI.Effects;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace HyPlayer.Shell.ExpandedPlayer.ExpandedCanvas;

/// <summary>
///     Draws opt-in timing diagnostics over the expanded player canvas.
/// </summary>
public sealed partial class DebugOverlayLayer : IExpandedCanvasLayer, IDisposable
{
    private const float OverlayWidth = 300;
    private const float OverlayHeight = 395;
    private const int WindowSeconds = 10;
    private const double TextRefreshMilliseconds = 250;
    private const double TrackChangeMilliseconds = 1500;
    private static readonly Color _overlayColor = Color.FromArgb(192, 0, 0, 0);
    private readonly ExpandedCanvasState _state;
    private readonly FrameStatisticsWindow _window = new(WindowSeconds);
    private readonly CanvasTextFormat _textFormat = new()
    {
        FontFamily = "Consolas",
        FontSize = 13,
        WordWrapping = CanvasWordWrapping.NoWrap
    };

    private double _framesPerSecond;
    private long _lastDrawTimestamp;
    private long _textTimestamp;
    private long _allocatedBytes;
    private int _threadId;
    private double _typicalInterval;
    private LyricFrameActivity _previousActivity;
    private bool _idle;
    private string _text = string.Empty;
    private CanvasTextLayout? _textLayout;
    private long _processAllocatedBytes;
    private long _processAllocatedTimestamp;
    private double _processAllocationRate;

    public DebugOverlayLayer(ExpandedCanvasState state)
    {
        _state = state;
    }

    public string LayerName => "Debug overlay";
    public int Order => int.MaxValue;
    public bool NeedsFrames => false;

    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        ReleaseTextLayout();
    }

    public void Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
    }

    public void Draw(ICanvasAnimatedControl sender, CanvasDrawingSession session, CanvasTimingInformation timing)
    {
        if (!_state.ShowDebugOverlay)
        {
            _lastDrawTimestamp = 0;
            _framesPerSecond = 0;
            _typicalInterval = 0;
            ReleaseTextLayout();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var threadId = Environment.CurrentManagedThreadId;
        var activity = _state.LyricBox?.LastFrameActivity ?? LyricFrameActivity.None;
        var idle = _state.IsCanvasIdle;
        if (_lastDrawTimestamp == 0 || threadId != _threadId)
        {
            _window.Reset(now);
        }
        else if (!idle && !_idle)
        {
            var interval = Stopwatch.GetElapsedTime(_lastDrawTimestamp, now).TotalMilliseconds;
            var expected = _state.FramePacer.FrameIntervalMilliseconds;
            var target = expected > 0 ? expected : _typicalInterval;
            var markers = FrameMarkerText.From(activity | _previousActivity, _state.IsTransitioning,
                Stopwatch.GetElapsedTime(_state.TrackChangeTimestamp, now).TotalMilliseconds < TrackChangeMilliseconds);
            _window.Add(now, interval, _state.LastFrameDrawMilliseconds, target > 0 && interval > target * 2,
                allocated - _allocatedBytes, markers);
            if (interval > 0)
            {
                var currentFramesPerSecond = 1000 / interval;
                _framesPerSecond = _framesPerSecond == 0
                    ? currentFramesPerSecond
                    : _framesPerSecond * 0.9 + currentFramesPerSecond * 0.1;
                _typicalInterval = _typicalInterval == 0
                    ? interval
                    : _typicalInterval + (Math.Min(interval, _typicalInterval * 2) - _typicalInterval) * 0.05;
            }
        }

        _idle = idle;
        _lastDrawTimestamp = now;
        _threadId = threadId;
        _previousActivity = activity;
        if (idle || _text.Length == 0 || Stopwatch.GetElapsedTime(_textTimestamp, now).TotalMilliseconds >= TextRefreshMilliseconds)
        {
            _textTimestamp = now;
            _text = Describe(sender, timing);
            ReleaseTextLayout();
        }

        var overlayX = Math.Max(8, (float)_state.Viewport(sender.Size).Width - OverlayWidth - 8);
        session.FillRectangle(overlayX, 8, OverlayWidth, OverlayHeight, _overlayColor);
        session.DrawRectangle(overlayX, 8, OverlayWidth, OverlayHeight, Colors.White, 1);
        _textLayout ??= new CanvasTextLayout(session, _text, _textFormat, OverlayWidth - 12, OverlayHeight - 8);
        session.DrawTextLayout(_textLayout, overlayX + 6, 12, Colors.White);
        _allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
    }

    private string Describe(ICanvasAnimatedControl sender, CanvasTimingInformation timing)
    {
        var pacer = _state.FramePacer;
        var timingMode = pacer.Mode switch
        {
            CanvasPacingMode.Capped => $"Capped 1/{pacer.Interval}",
            CanvasPacingMode.Variable => "Variable",
            _ => "Calibrating"
        };
        var refreshRate = pacer.RefreshPeriodMilliseconds > 0 ? 1000 / pacer.RefreshPeriodMilliseconds : 0;
        var runningState = _state.IsCanvasIdle ? "暂停" : timing.IsRunningSlowly ? "SLOW" : "OK";
        var window = _window.Summarize();
        var now = Stopwatch.GetTimestamp();
        var processAllocated = GC.GetTotalAllocatedBytes(false);
        if (_processAllocatedTimestamp != 0)
        {
            var seconds = Stopwatch.GetElapsedTime(_processAllocatedTimestamp, now).TotalSeconds;
            if (seconds > 0)
                _processAllocationRate = _processAllocationRate * 0.75 +
                                         (processAllocated - _processAllocatedBytes) / seconds * 0.25;
        }

        _processAllocatedBytes = processAllocated;
        _processAllocatedTimestamp = now;
        var last = GC.GetGCMemoryInfo(GCKind.Any);
        var lastPause = 0.0;
        foreach (var pause in last.PauseDurations) lastPause += pause.TotalMilliseconds;
        var lastKind = last.Concurrent ? "后台" : last.Compacted ? "阻塞压缩" : "阻塞";
        var largeObjects = last.GenerationInfo.Length > 3 ? last.GenerationInfo[3].SizeAfterBytes : 0;
        var memoryLoad = last.TotalAvailableMemoryBytes > 0
            ? 100.0 * last.MemoryLoadBytes / last.TotalAvailableMemoryBytes
            : 0;
        var highLoad = last.TotalAvailableMemoryBytes > 0
            ? 100.0 * last.HighMemoryLoadThresholdBytes / last.TotalAvailableMemoryBytes
            : 0;
        return
            $"ExpandedPlayer / Win2D\n" +
            $"FPS (Draw)       {_framesPerSecond,8:F1}\n" +
            $"帧绘制           {_state.LastFrameDrawMilliseconds,8:F2} ms\n" +
            $"更新间隔         {timing.ElapsedTime.TotalMilliseconds,8:F2} ms\n" +
            $"目标间隔         {pacer.FrameIntervalMilliseconds,8:F2} ms\n" +
            $"运行总时长       {timing.TotalTime.TotalSeconds,8:F1} s\n" +
            $"更新计数         {timing.UpdateCount,8}\n" +
            $"计时状态         {timingMode} / {runningState}\n" +
            $"显示刷新率       {refreshRate,8:F1} Hz\n" +
            $"画布 (DIP)       {sender.Size.Width:F0} x {sender.Size.Height:F0}\n" +
            $"DPI 缩放         {_state.RasterizationScale,8:F2}x\n" +
            $"-- 最近 {WindowSeconds} 秒 --\n" +
            $"最长帧间隔       {window.LongestInterval,8:F2} ms\n" +
            $"最长帧绘制       {window.LongestDraw,8:F2} ms\n" +
            $"超 2 倍间隔帧    {window.LongFrames,8} / {window.Frames}\n" +
            $"每帧分配         {window.AllocatedPerFrame,8:F0} B\n" +
            $"GC 0/1/2 代      {window.Generation0,4} / {window.Generation1} / {window.Generation2}\n" +
            $"GC 暂停          {window.PauseMilliseconds,8:F2} ms\n" +
            $"上次 GC          {last.Generation} 代 {lastKind} {lastPause:F1} ms\n" +
            $"托管堆 / 大对象  {last.HeapSizeBytes / 1048576.0,6:F0} / {largeObjects / 1048576.0:F0} MB\n" +
            $"进程分配速率     {_processAllocationRate / 1048576.0,8:F2} MB/s\n" +
            $"系统内存负载     {memoryLoad,5:F0} % (高 {highLoad:F0} %)\n" +
            $"解码缓冲 建/还   {System.Threading.Interlocked.Read(ref HyPlayer.Platform.Playback.MediaSources.DecodeBufferStatistics.Created)} / {System.Threading.Interlocked.Read(ref HyPlayer.Platform.Playback.MediaSources.DecodeBufferStatistics.Returned)}\n" +
            $"最差帧           {window.WorstMarkers.Describe()}";
    }

    public bool TryHandlePointer(PointerRoutedEventArgs args)
    {
        return false;
    }

    private void ReleaseTextLayout()
    {
        _textLayout?.Dispose();
        _textLayout = null;
    }

    public void Dispose()
    {
        ReleaseTextLayout();
        _textFormat.Dispose();
    }
}
