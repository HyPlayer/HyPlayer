#nullable enable
using CommunityToolkit.WinUI;
using System;
using System.Numerics;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace HyPlayer.UI.Controls.Primitives;

public sealed class PlayBarTransitionEffect
{
    private readonly Slider _slider;
    private readonly Grid _tag;
    private readonly TextBlock _qualityText;
    private readonly TextBlock _transitionText;
    private readonly FrameworkElement _textGlowHost;
    private Rectangle? _track;
    private Rectangle? _fill;
    private ContainerVisual? _progressGlow;
    private SpriteVisual? _core;
    private SpriteVisual? _bloom;
    private DropShadow? _coreShadow;
    private DropShadow? _bloomShadow;
    private SpriteVisual? _textGlow;
    private DropShadow? _textShadow;
    private Brush? _tagBorder;
    private bool _active;

    public PlayBarTransitionEffect(Slider slider, Grid tag, TextBlock qualityText, TextBlock transitionText, FrameworkElement textGlowHost)
    {
        _slider = slider;
        _tag = tag;
        _qualityText = qualityText;
        _transitionText = transitionText;
        _textGlowHost = textGlowHost;
        Visual(_transitionText).Opacity = 0;
        _transitionText.SizeChanged += (_, _) => LayoutTextGlow();
    }

    private static Visual Visual(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        var color = (_slider.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        EnsureProgressGlow();
        EnsureTextGlow();

        if (_coreShadow is not null) _coreShadow.Color = color;
        if (_bloomShadow is not null) _bloomShadow.Color = color;
        if (_progressGlow is not null)
        {
            LayoutProgressGlow();
            Fade(_progressGlow, active ? 1 : 0, active ? 700 : 800);
        }

        if (active)
        {
            _tagBorder ??= _tag.BorderBrush;
            _tag.BorderBrush = _slider.Foreground;
            _transitionText.Foreground = _slider.Foreground;
        }
        else if (_tagBorder is not null)
        {
            _tag.BorderBrush = _tagBorder;
        }
        Fade(Visual(_qualityText), active ? 0 : 1, 250);
        Fade(Visual(_transitionText), active ? 1 : 0, 250);

        if (_textGlow is null || _textShadow is null) return;
        _textShadow.Color = color;
        LayoutTextGlow();
        var compositor = _textGlow.Compositor;
        if (active)
        {
            var breathe = compositor.CreateScalarKeyFrameAnimation();
            breathe.InsertKeyFrame(0f, 0.25f);
            breathe.InsertKeyFrame(0.5f, 0.95f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(0.58f, 1f)));
            breathe.InsertKeyFrame(1f, 0.25f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(0.58f, 1f)));
            breathe.Duration = TimeSpan.FromMilliseconds(1800);
            breathe.IterationBehavior = AnimationIterationBehavior.Forever;
            _textShadow.StartAnimation("Opacity", breathe);
            Fade(_textGlow, 1, 300);
        }
        else
        {
            Fade(_textGlow, 0, 400);
        }
    }

    private static void Fade(Visual visual, float to, int milliseconds)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to);
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", animation);
    }

    private void EnsureProgressGlow()
    {
        if (_progressGlow is not null) return;
        _track ??= _slider.FindDescendant<Rectangle>(r => r.Name == "HorizontalTrackRect");
        _fill ??= _slider.FindDescendant<Rectangle>(r => r.Name == "HorizontalDecreaseRect");
        if (_track is null || _fill is null) return;
        var compositor = Visual(_track).Compositor;
        _progressGlow = compositor.CreateContainerVisual();
        _progressGlow.Opacity = 0;
        (_bloom, _bloomShadow) = CreateGlowLayer(compositor, 10, 0.5f);
        (_core, _coreShadow) = CreateGlowLayer(compositor, 4, 0.85f);
        _progressGlow.Children.InsertAtTop(_bloom);
        _progressGlow.Children.InsertAtTop(_core);
        ElementCompositionPreview.SetElementChildVisual(_track, _progressGlow);
        _fill.SizeChanged += (_, _) => LayoutProgressGlow();
    }

    private void LayoutProgressGlow()
    {
        if (_fill is null || _core is null || _bloom is null) return;
        var size = new Vector2((float)Math.Max(0, _fill.ActualWidth), (float)_fill.ActualHeight);
        _core.Size = size;
        _bloom.Size = size;
    }

    private void EnsureTextGlow()
    {
        if (_textGlow is not null) return;
        var compositor = Visual(_textGlowHost).Compositor;
        _textShadow = compositor.CreateDropShadow();
        _textShadow.BlurRadius = 6;
        _textShadow.Offset = Vector3.Zero;
        _textShadow.Mask = _transitionText.GetAlphaMask();
        _textGlow = compositor.CreateSpriteVisual();
        _textGlow.Shadow = _textShadow;
        _textGlow.Opacity = 0;
        ElementCompositionPreview.SetElementChildVisual(_textGlowHost, _textGlow);
    }

    private void LayoutTextGlow()
    {
        if (_textGlow is null) return;
        var origin = _transitionText.TransformToVisual(_textGlowHost).TransformPoint(new Windows.Foundation.Point(0, 0));
        _textGlow.Offset = new Vector3((float)origin.X, (float)origin.Y, 0);
        _textGlow.Size = new Vector2((float)_transitionText.ActualWidth, (float)_transitionText.ActualHeight);
    }

    private static (SpriteVisual Visual, DropShadow Shadow) CreateGlowLayer(Compositor compositor, float blur, float opacity)
    {
        var shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = blur;
        shadow.Opacity = opacity;
        shadow.Offset = Vector3.Zero;
        var visual = compositor.CreateSpriteVisual();
        visual.Shadow = shadow;
        return (visual, shadow);
    }
}
