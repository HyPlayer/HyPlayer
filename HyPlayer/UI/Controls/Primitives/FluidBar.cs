using System;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Shapes;

namespace HyPlayer.UI.Controls.Primitives;

public sealed partial class FluidBar : Grid
{
    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(FluidBar),
            new PropertyMetadata(null, OnBarBrushChanged));

    private const double IdleScale = 0.5;

    private readonly Grid _bar = new() { Height = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly Rectangle _track = new() { RadiusX = 4, RadiusY = 4, Opacity = 0.25 };

    private readonly Rectangle _fill = new()
        { RadiusX = 4, RadiusY = 4, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };

    private readonly ScaleTransform _scale = new() { ScaleY = IdleScale };

    private readonly DoubleAnimation _scaleAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(180),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private readonly Storyboard _scaleStoryboard = new();

    private double _value;
    private double _dragValue;
    private bool _isHovering;

    public FluidBar()
    {
        Height = 24;
        Background = new SolidColorBrush(Colors.Transparent);
        _bar.RenderTransformOrigin = new Point(0.5, 0.5);
        _bar.RenderTransform = _scale;
        _bar.Children.Add(_track);
        _bar.Children.Add(_fill);
        Children.Add(_bar);

        Storyboard.SetTarget(_scaleAnimation, _scale);
        Storyboard.SetTargetProperty(_scaleAnimation, "ScaleY");
        _scaleStoryboard.Children.Add(_scaleAnimation);

        _bar.SizeChanged += (_, _) => UpdateFill();
        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        Tapped += (_, e) => e.Handled = true;
        DoubleTapped += (_, e) => e.Handled = true;
    }

    public event Action<double>? ValueDragging;
    public event Action<double>? ValueCommitted;

    public Brush? BarBrush
    {
        get => (Brush?)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public bool IsDragging { get; private set; }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);
            if (!IsDragging) UpdateFill();
        }
    }

    private static void OnBarBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FluidBar)d;
        bar._track.Fill = e.NewValue as Brush;
        bar._fill.Fill = e.NewValue as Brush;
    }

    private void UpdateFill()
    {
        var width = _bar.ActualWidth;
        if (width <= 0) return;
        _fill.Width = width * (IsDragging ? _dragValue : _value);
    }

    private void AnimateScale(double to)
    {
        _scaleAnimation.To = to;
        _scaleStoryboard.Begin();
    }

    private void RefreshScale()
    {
        AnimateScale(_isHovering || IsDragging ? 1 : IdleScale);
    }

    private double GetPointerValue(PointerRoutedEventArgs e)
    {
        var width = _bar.ActualWidth;
        if (width <= 0) return _value;
        return Math.Clamp(e.GetCurrentPoint(_bar).Position.X / width, 0, 1);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = true;
        RefreshScale();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovering = false;
        RefreshScale();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) return;
        CapturePointer(e.Pointer);
        IsDragging = true;
        _dragValue = GetPointerValue(e);
        UpdateFill();
        RefreshScale();
        ValueDragging?.Invoke(_dragValue);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) return;
        _dragValue = GetPointerValue(e);
        UpdateFill();
        ValueDragging?.Invoke(_dragValue);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) return;
        _dragValue = GetPointerValue(e);
        EndDrag();
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (IsDragging) EndDrag();
    }

    private void EndDrag()
    {
        IsDragging = false;
        _value = _dragValue;
        UpdateFill();
        RefreshScale();
        ValueCommitted?.Invoke(_value);
    }
}
