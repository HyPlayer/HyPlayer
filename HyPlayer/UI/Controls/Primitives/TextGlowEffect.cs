using System;
using System.Numerics;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace HyPlayer.UI.Controls.Primitives;

public sealed class TextGlowEffect
{
    private const float Edge = 16;
    private const double CycleMilliseconds = 3000;

    private readonly FrameworkElement _revealHost;
    private readonly UIElement _maskSource;
    private readonly TextBlock _revealText;
    private readonly UIElement _bloomHost;
    private readonly UIElement _fillHost;
    private Visual? _hostVisual;
    private SpriteVisual? _bloom;
    private SpriteVisual? _fill;
    private CompositionLinearGradientBrush? _gradient;
    private CompositionLinearGradientBrush? _bloomGradient;
    private static readonly Color Dim = Color.FromArgb(89, 255, 255, 255);
    private static readonly Color Clear = Color.FromArgb(0, 255, 255, 255);
    private bool _active;

    public TextGlowEffect(FrameworkElement revealHost, UIElement maskSource, TextBlock revealText, UIElement bloomHost, UIElement fillHost)
    {
        _revealHost = revealHost;
        _maskSource = maskSource;
        _revealText = revealText;
        _bloomHost = bloomHost;
        _fillHost = fillHost;
        _revealText.SizeChanged += (_, _) =>
        {
            Layout();
            if (_active) Start();
        };
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        Ensure();
        var compositor = _hostVisual!.Compositor;
        if (active)
        {
            _revealHost.Visibility = Visibility.Visible;
            _hostVisual.StopAnimation("Opacity");
            _hostVisual.Opacity = 1;
            Layout();
            Start();
            return;
        }
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = TimeSpan.FromMilliseconds(400);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _hostVisual.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (_active) return;
            _gradient?.StopAnimation("Offset");
            _bloomGradient?.StopAnimation("Offset");
            _revealHost.Visibility = Visibility.Collapsed;
        };
    }

    private void Ensure()
    {
        if (_hostVisual is not null) return;
        _hostVisual = ElementCompositionPreview.GetElementVisual(_revealHost);
        var compositor = _hostVisual.Compositor;
        ElementCompositionPreview.GetElementVisual(_maskSource).Opacity = 0;

        var textMask = _revealText.GetAlphaMask();
        _gradient = CreateWindowGradient(compositor, Dim);
        _bloomGradient = CreateWindowGradient(compositor, Clear);
        var mask = compositor.CreateMaskBrush();
        mask.Source = _gradient;
        mask.Mask = textMask;
        var bloomMask = compositor.CreateMaskBrush();
        bloomMask.Source = _bloomGradient;
        bloomMask.Mask = textMask;

        var bloomShadow = compositor.CreateDropShadow();
        bloomShadow.Color = Colors.White;
        bloomShadow.BlurRadius = 4;
        bloomShadow.Opacity = 0.85f;
        bloomShadow.Offset = Vector3.Zero;
        bloomShadow.Mask = bloomMask;
        _bloom = compositor.CreateSpriteVisual();
        _bloom.Shadow = bloomShadow;
        ElementCompositionPreview.SetElementChildVisual(_bloomHost, _bloom);

        _fill = compositor.CreateSpriteVisual();
        _fill.Brush = mask;
        ElementCompositionPreview.SetElementChildVisual(_fillHost, _fill);
    }

    private static CompositionLinearGradientBrush CreateWindowGradient(Compositor compositor, Color outside)
    {
        var gradient = compositor.CreateLinearGradientBrush();
        gradient.MappingMode = CompositionMappingMode.Absolute;
        gradient.ExtendMode = CompositionGradientExtendMode.Clamp;
        gradient.StartPoint = Vector2.Zero;
        foreach (var offset in new[] { 0f, 0.25f, 0.75f, 1f })
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(offset, offset is 0f or 1f ? outside : Colors.White));
        return gradient;
    }

    private void Layout()
    {
        if (_bloom is null || _fill is null) return;
        var size = new Vector2((float)_revealText.ActualWidth, (float)_revealText.ActualHeight);
        _bloom.Size = size;
        _fill.Size = size;
    }

    private void Start()
    {
        if (_gradient is null || _bloomGradient is null) return;
        var compositor = _gradient.Compositor;
        var width = (float)Math.Max(_revealText.ActualWidth, 1);
        var plateau = width + Edge;
        var length = Edge + plateau + Edge;
        foreach (var gradient in new[] { _gradient, _bloomGradient })
        {
            gradient.EndPoint = new Vector2(length, 0);
            gradient.ColorStops[1].Offset = Edge / length;
            gradient.ColorStops[2].Offset = (Edge + plateau) / length;
        }

        var fillEase = compositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
        var sweep = compositor.CreateVector2KeyFrameAnimation();
        sweep.InsertKeyFrame(0f, new Vector2(-length, 0));
        sweep.InsertKeyFrame(0.35f, new Vector2(-2 * Edge, 0), fillEase);
        sweep.InsertKeyFrame(0.55f, new Vector2(-Edge, 0));
        sweep.InsertKeyFrame(0.9f, new Vector2(width, 0), fillEase);
        sweep.InsertKeyFrame(1f, new Vector2(width, 0));
        sweep.Duration = TimeSpan.FromMilliseconds(CycleMilliseconds);
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        _gradient.StartAnimation("Offset", sweep);
        _bloomGradient.StartAnimation("Offset", sweep);
    }
}
