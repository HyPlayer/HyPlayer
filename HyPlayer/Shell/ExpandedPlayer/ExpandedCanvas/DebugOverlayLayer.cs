using System;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml.Input;
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
    private const float OverlayWidth = 280;
    private const float OverlayHeight = 174;
    private static readonly Color _overlayColor = Color.FromArgb(192, 0, 0, 0);
    private readonly ExpandedCanvasState _state;
    private readonly CanvasTextFormat _textFormat = new()
    {
        FontFamily = "Consolas",
        FontSize = 13,
        WordWrapping = CanvasWordWrapping.NoWrap
    };

    private double _framesPerSecond;
    private long _lastDrawTimestamp;

    public DebugOverlayLayer(ExpandedCanvasState state)
    {
        _state = state;
    }

    public string LayerName => "Debug overlay";
    public int Order => int.MaxValue;

    public void CreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
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
            return;
        }

        UpdateFrameRate();

        var timingMode = sender.IsFixedTimeStep ? "Fixed" : "Variable";
        var runningState = timing.IsRunningSlowly ? "SLOW" : "OK";
        var diagnostics =
            $"ExpandedPlayer / Win2D\n" +
            $"FPS (Draw)       {_framesPerSecond,8:F1}\n" +
            $"帧绘制           {_state.LastFrameDrawMilliseconds,8:F2} ms\n" +
            $"更新间隔         {timing.ElapsedTime.TotalMilliseconds,8:F2} ms\n" +
            $"目标间隔         {sender.TargetElapsedTime.TotalMilliseconds,8:F2} ms\n" +
            $"运行总时长       {timing.TotalTime.TotalSeconds,8:F1} s\n" +
            $"更新计数         {timing.UpdateCount,8}\n" +
            $"计时状态         {timingMode,8} / {runningState}\n" +
            $"画布 (DIP)       {sender.Size.Width:F0} x {sender.Size.Height:F0}\n" +
            $"DPI 缩放         {sender.DpiScale,8:F2}x";

        var overlayX = Math.Max(8, (float)sender.Size.Width - OverlayWidth - 8);
        session.FillRectangle(overlayX, 8, OverlayWidth, OverlayHeight, _overlayColor);
        session.DrawRectangle(overlayX, 8, OverlayWidth, OverlayHeight, Colors.White, 1);
        session.DrawText(diagnostics, new Rect(overlayX + 6, 12, OverlayWidth - 12, OverlayHeight - 8), Colors.White,
            _textFormat);
    }

    public bool TryHandlePointer(PointerRoutedEventArgs args)
    {
        return false;
    }

    public void Dispose()
    {
        _textFormat.Dispose();
    }

    private void UpdateFrameRate()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastDrawTimestamp != 0)
        {
            var frameSeconds = Stopwatch.GetElapsedTime(_lastDrawTimestamp, now).TotalSeconds;
            if (frameSeconds > 0)
            {
                var currentFramesPerSecond = 1 / frameSeconds;
                _framesPerSecond = _framesPerSecond == 0
                    ? currentFramesPerSecond
                    : _framesPerSecond * 0.9 + currentFramesPerSecond * 0.1;
            }
        }

        _lastDrawTimestamp = now;
    }
}
