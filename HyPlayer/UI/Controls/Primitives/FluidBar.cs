using System;
using System.Numerics;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
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

    private readonly Grid _glowHost = new() { IsHitTestVisible = false };
    private ContainerVisual? _glowRoot;
    private (SpriteVisual Visual, DropShadow Shadow)? _glowCore;
    private (SpriteVisual Visual, DropShadow Shadow)? _glowBloom;
    private bool _glowing;

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
        Children.Add(_glowHost);
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

    public bool IsGlowing
    {
        get => _glowing;
        set
        {
            if (_glowing == value) return;
            _glowing = value;
            RefreshScale();
            EnsureGlow();
            UpdateGlowLayout();
            var compositor = _glowRoot!.Compositor;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, value ? 1f : 0f,
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.25f, 0.1f), new Vector2(0.25f, 1f)));
            fade.Duration = TimeSpan.FromMilliseconds(value ? 700 : 800);
            _glowRoot.StartAnimation("Opacity", fade);
        }
    }

    private static void OnBarBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (FluidBar)d;
        bar._track.Fill = e.NewValue as Brush;
        bar._fill.Fill = e.NewValue as Brush;
        bar.UpdateGlowColor();
    }

    private void UpdateFill()
    {
        var width = _bar.ActualWidth;
        if (width <= 0) return;
        _fill.Width = width * (IsDragging ? _dragValue : _value);
        UpdateGlowLayout();
    }

    private void EnsureGlow()
    {
        if (_glowRoot is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _glowRoot = compositor.CreateContainerVisual();
        _glowRoot.Opacity = 0;
        _glowCore = CreateGlowLayer(compositor, 5, 0.9f);
        _glowBloom = CreateGlowLayer(compositor, 11, 0.45f);
        _glowRoot.Children.InsertAtTop(_glowBloom.Value.Visual);
        _glowRoot.Children.InsertAtTop(_glowCore.Value.Visual);
        UpdateGlowColor();
        ElementCompositionPreview.SetElementChildVisual(_glowHost, _glowRoot);
    }

    private static (SpriteVisual Visual, DropShadow Shadow) CreateGlowLayer(
        Compositor compositor,
        float blur,
        float opacity)
    {
        var shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = blur;
        shadow.Opacity = opacity;
        shadow.Offset = Vector3.Zero;
        shadow.Color = Colors.White;
        var visual = compositor.CreateSpriteVisual();
        visual.Shadow = shadow;
        return (visual, shadow);
    }

    private void UpdateGlowColor()
    {
        if (_glowCore is null || _glowBloom is null) return;
        var color = BarBrush is SolidColorBrush brush ? brush.Color : Colors.White;
        color.A = 255;
        _glowCore.Value.Shadow.Color = color;
        _glowBloom.Value.Shadow.Color = color;
    }

    private void UpdateGlowLayout()
    {
        if (_glowCore is null || _glowBloom is null) return;
        var height = (float)_bar.Height;
        var size = new Vector2((float)Math.Max(_fill.Width, 0), height);
        var offset = new Vector3(0, (float)(ActualHeight - height) / 2, 0);
        _glowCore.Value.Visual.Size = size;
        _glowCore.Value.Visual.Offset = offset;
        _glowBloom.Value.Visual.Size = size;
        _glowBloom.Value.Visual.Offset = offset;
    }

    private void AnimateScale(double to)
    {
        _scaleAnimation.To = to;
        _scaleStoryboard.Begin();
    }

    private void RefreshScale()
    {
        AnimateScale(_isHovering || IsDragging || _glowing ? 1 : IdleScale);
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
