#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Windows.Devices.Input;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using CommunityToolkit.WinUI;

namespace HyPlayer.UI.Behaviors;

public static class HorizontalWheelScroll
{
    public static readonly DependencyProperty IsAnimatedProperty =
        DependencyProperty.RegisterAttached("IsAnimated", typeof(bool), typeof(HorizontalWheelScroll),
            new PropertyMetadata(false, OnIsAnimatedChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, Scroller> Scrollers = new();

    public static bool GetIsAnimated(DependencyObject element) => (bool)element.GetValue(IsAnimatedProperty);

    public static void SetIsAnimated(DependencyObject element, bool value) => element.SetValue(IsAnimatedProperty, value);

    private static void OnIsAnimatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host) return;
        if (Scrollers.TryGetValue(host, out var existing))
        {
            existing.Dispose();
            Scrollers.Remove(host);
        }

        if (e.NewValue is true) Scrollers.Add(host, new Scroller(host));
    }

    private sealed class Scroller
    {
        private static UISettings? _systemSettings;

        private readonly HorizontalWheelScrollAnimation _animation = new();
        private readonly PointerEventHandler _contentWheelHandler;
        private readonly FrameworkElement _host;
        private readonly PointerEventHandler _hostWheelHandler;
        private readonly KeyEventHandler _keyDownHandler;
        private readonly EventHandler<object> _manipulationHandler;
        private readonly PointerEventHandler _pressedHandler;
        private readonly EventHandler<object> _renderingHandler;
        private UIElement? _content;
        private long _lastTimestamp;
        private bool _passThrough;
        private bool _rendering;
        private ScrollViewer? _scrollViewer;
        private double _unitsPerPixel = 1;

        public Scroller(FrameworkElement host)
        {
            _host = host;
            _contentWheelHandler = OnContentWheelChanged;
            _hostWheelHandler = OnHostWheelChanged;
            _keyDownHandler = OnHostKeyDown;
            _manipulationHandler = OnManipulationStarted;
            _pressedHandler = OnHostPointerPressed;
            _renderingHandler = OnRendering;
            host.Loaded += OnLoaded;
            host.Unloaded += OnUnloaded;
            if (host.IsLoaded) Attach();
        }

        private static bool AnimationsEnabled => (_systemSettings ??= new UISettings()).AnimationsEnabled;

        public void Dispose()
        {
            _host.Loaded -= OnLoaded;
            _host.Unloaded -= OnUnloaded;
            Detach();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Attach()
        {
            if (_scrollViewer is not null) return;
            if (_host is Control control) control.ApplyTemplate();
            var scrollViewer = _host as ScrollViewer ?? _host.FindDescendant<ScrollViewer>();
            if (scrollViewer?.Content is not UIElement content) return;
            _scrollViewer = scrollViewer;
            _content = content;
            content.AddHandler(UIElement.PointerWheelChangedEvent, _contentWheelHandler, false);
            _host.AddHandler(UIElement.PointerWheelChangedEvent, _hostWheelHandler, true);
            _host.AddHandler(UIElement.PointerPressedEvent, _pressedHandler, true);
            _host.AddHandler(UIElement.KeyDownEvent, _keyDownHandler, true);
            scrollViewer.DirectManipulationStarted += _manipulationHandler;
        }

        private void Detach()
        {
            StopAnimation();
            _passThrough = false;
            if (_scrollViewer is not { } scrollViewer) return;
            _content?.RemoveHandler(UIElement.PointerWheelChangedEvent, _contentWheelHandler);
            _host.RemoveHandler(UIElement.PointerWheelChangedEvent, _hostWheelHandler);
            _host.RemoveHandler(UIElement.PointerPressedEvent, _pressedHandler);
            _host.RemoveHandler(UIElement.KeyDownEvent, _keyDownHandler);
            scrollViewer.DirectManipulationStarted -= _manipulationHandler;
            _scrollViewer = null;
            _content = null;
        }

        private void OnContentWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            _passThrough = false;
            if (e.Handled || _scrollViewer is not { } scrollViewer) return;
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
            if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) return;
            if (scrollViewer.HorizontalScrollMode == ScrollMode.Disabled) return;

            var properties = e.GetCurrentPoint(scrollViewer).Properties;
            var wheelDelta = properties.MouseWheelDelta;
            if (!HorizontalWheelScrollAnimation.IsNotch(wheelDelta)) return;
            if (!properties.IsHorizontalMouseWheel && scrollViewer.ScrollableHeight > 0) return;

            var viewportPixels = scrollViewer.ActualWidth;
            if (viewportPixels <= 0 || scrollViewer.ViewportWidth <= 0) return;
            if (!_animation.IsRunning) _unitsPerPixel = scrollViewer.ViewportWidth / viewportPixels;

            var direction = HorizontalWheelScrollAnimation.DirectionOf(wheelDelta, properties.IsHorizontalMouseWheel);
            var step = HorizontalWheelScrollAnimation.StepFor(viewportPixels, wheelDelta) * direction;
            var outcome = _animation.Push(scrollViewer.HorizontalOffset / _unitsPerPixel,
                scrollViewer.ScrollableWidth / _unitsPerPixel, step);
            if (outcome == WheelScrollOutcome.PassThrough)
            {
                _passThrough = true;
                return;
            }

            e.Handled = true;
            if (!AnimationsEnabled)
            {
                StopRendering();
                Apply(_animation.Complete());
                return;
            }

            _lastTimestamp = Stopwatch.GetTimestamp();
            if (_animation.IsRunning) StartRendering();
        }

        private void OnHostWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!_passThrough) return;
            _passThrough = false;
            e.Handled = false;
        }

        private void OnHostPointerPressed(object sender, PointerRoutedEventArgs e) => StopAnimation();

        private void OnHostKeyDown(object sender, KeyRoutedEventArgs e) => StopAnimation();

        private void OnManipulationStarted(object? sender, object e) => StopAnimation();

        private void OnRendering(object? sender, object e)
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = now;
            Apply(_animation.Advance(seconds));
            if (!_animation.IsRunning) StopRendering();
        }

        private void Apply(double position)
        {
            _scrollViewer?.ChangeView(position * _unitsPerPixel, null, null, true);
        }

        private void StartRendering()
        {
            if (_rendering) return;
            _rendering = true;
            CompositionTarget.Rendering += _renderingHandler;
        }

        private void StopRendering()
        {
            if (!_rendering) return;
            _rendering = false;
            CompositionTarget.Rendering -= _renderingHandler;
        }

        private void StopAnimation()
        {
            _animation.Stop();
            StopRendering();
        }
    }
}
