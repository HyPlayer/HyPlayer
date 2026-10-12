using System;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;

namespace HyPlayer.UI.Controls.Primitives;

public sealed class PressSpring
{
    public const float PressedScale = 0.9f;
    public const float PulseScale = 0.94f;
    public const float PressDamping = 0.75f;
    public const float ReleaseDamping = 0.5f;
    public const float NudgeVelocity = 220;
    public static readonly TimeSpan PressPeriod = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan ReleasePeriod = TimeSpan.FromMilliseconds(240);
    public static readonly TimeSpan NudgePeriod = TimeSpan.FromMilliseconds(260);

    private readonly ButtonBase _button;
    private readonly Func<bool> _animationsEnabled;
    private bool _pressed;
    private long _pointerReleasedAt = long.MinValue / 2;

    public PressSpring(ButtonBase button, Func<bool> animationsEnabled)
    {
        _button = button;
        _animationsEnabled = animationsEnabled;
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        button.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
    }

    public void Pulse()
    {
        if (_pressed || Environment.TickCount64 - _pointerReleasedAt < 400 || !_animationsEnabled()) return;
        Start(_button, "Scale", Vector3.One, ReleaseDamping, ReleasePeriod,
            new Vector3(PulseScale, PulseScale, 1));
    }

    public static void Nudge(FrameworkElement element, float direction)
    {
        var spring = ElementCompositionPreview.GetElementVisual(element).Compositor.CreateSpringVector3Animation();
        spring.Target = "Translation";
        spring.FinalValue = Vector3.Zero;
        spring.InitialVelocity = new Vector3(direction * NudgeVelocity, 0, 0);
        spring.DampingRatio = ReleaseDamping;
        spring.Period = NudgePeriod;
        element.StartAnimation(spring);
    }

    public static void Start(FrameworkElement element, string target, Vector3 finalValue, float dampingRatio,
        TimeSpan period, Vector3? initialValue = null)
    {
        element.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var spring = ElementCompositionPreview.GetElementVisual(element).Compositor.CreateSpringVector3Animation();
        spring.Target = target;
        spring.FinalValue = finalValue;
        if (initialValue is { } start) spring.InitialValue = start;
        spring.DampingRatio = dampingRatio;
        spring.Period = period;
        element.StartAnimation(spring);
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_pressed || !_button.IsEnabled || !_animationsEnabled()) return;
        _pressed = true;
        Start(_button, "Scale", new Vector3(PressedScale, PressedScale, 1), PressDamping, PressPeriod);
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        _pointerReleasedAt = Environment.TickCount64;
        Start(_button, "Scale", Vector3.One, ReleaseDamping, ReleasePeriod);
    }
}
