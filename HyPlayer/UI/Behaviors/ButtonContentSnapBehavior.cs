#nullable enable

using System;
using System.Linq;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;
using Microsoft.Xaml.Interactivity;
using WinRT;

namespace HyPlayer.UI.Behaviors;

public class ButtonContentSnapBehavior : Behavior<ButtonBase>
{
    private const double DurationSeconds = 0.3;

    public static readonly DependencyProperty SnapTypeProperty =
        DependencyProperty.Register("SnapType", typeof(ButtonContentSnapType), typeof(ButtonContentSnapBehavior),
            new PropertyMetadata(ButtonContentSnapType.None, (s, a) =>
            {
                if (s is ButtonContentSnapBehavior sender && !Equals(a.NewValue, a.OldValue)) sender.UpdateSnapType();
            }));

    private readonly Compositor _compositor;
    private readonly CompositionPropertySet _propSet;
    private readonly Version _supportedVersion = new(10, 0, 20348, 0);
    private readonly Vector3KeyFrameAnimation _translationAnimation1;
    private readonly Vector3KeyFrameAnimation _translationAnimation2;

    private bool _attached;
    private long _contentChangedEventToken;
    private ContentPresenter? _contentPresenter;
    private Visual? _contentVisual;
    private long _paddingChangedEventToken;
    private VisualStateGroup? _visualStateGroup;

    public ButtonContentSnapBehavior()
    {
        _compositor = Window.Current.Compositor;

        _propSet = _compositor.CreatePropertySet();
        _propSet.InsertVector3("Offset", Vector3.Zero);

        _translationAnimation1 = _compositor.CreateVector3KeyFrameAnimation();
        _translationAnimation1.InsertKeyFrame(1, Vector3.Zero);
        _translationAnimation1.Duration = TimeSpan.FromSeconds(DurationSeconds);
        _translationAnimation1.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;

        _translationAnimation2 = _compositor.CreateVector3KeyFrameAnimation();
        _translationAnimation2.InsertExpressionKeyFrame(1, "propSet.Offset");
        _translationAnimation2.Duration = TimeSpan.FromSeconds(DurationSeconds);
        _translationAnimation2.SetReferenceParameter("propSet", _propSet);
        _translationAnimation2.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
    }

    public ButtonContentSnapType SnapType
    {
        get => (ButtonContentSnapType)GetValue(SnapTypeProperty);
        set => SetValue(SnapTypeProperty, value);
    }

    private void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
    {
        TryLoadContent((ButtonBase)sender);
    }

    private void AssociatedObject_Unloaded(object sender, RoutedEventArgs e)
    {
        UnloadContent((ButtonBase)sender);
    }

    private void AssociatedObject_LayoutUpdated(object? sender, object e)
    {
        TryLoadContent(AssociatedObject);
    }

    private void TryLoadContent(ButtonBase? button)
    {
        if (button == null) return;

        button.LayoutUpdated -= AssociatedObject_LayoutUpdated;

        if (button.IsLoaded)
        {
            if (VisualTreeHelper.GetChildrenCount(button) > 0)
                LoadContent(button);
            else
                button.LayoutUpdated += AssociatedObject_LayoutUpdated;
        }
        else
        {
            UnloadContent(button);
        }
    }

    private void LoadContent(ButtonBase button)
    {
        if (_attached) return;

        _paddingChangedEventToken =
            button.RegisterPropertyChangedCallback(Control.PaddingProperty, OnPaddingPropertyChanged);
        _visualStateGroup = VisualStateManager
            .GetVisualStateGroups((FrameworkElement)VisualTreeHelper.GetChild(button, 0))
            .FirstOrDefault(c => c.Name == "CommonStates");

        _visualStateGroup?.CurrentStateChanging += VisualStateGroup_CurrentStateChanging;

        _contentPresenter = FindChild<ContentPresenter>(button);

        if (_contentPresenter != null)
        {
            _contentChangedEventToken =
                _contentPresenter.RegisterPropertyChangedCallback(ContentPresenter.ContentProperty, OnContentChanged);
            var actualContent = VisualTreeHelper.GetChild(_contentPresenter, 0)?.As<UIElement>();

            if (actualContent != null)
            {
                _contentVisual = ElementCompositionPreview.GetElementVisual(actualContent);
                if (Environment.OSVersion.Version >= _supportedVersion)
#pragma warning disable CA1416 // 验证平台兼容性
                    _contentVisual.IsPixelSnappingEnabled = true;
#pragma warning restore CA1416 // 验证平台兼容性
                ElementCompositionPreview.SetIsTranslationEnabled(actualContent, true);
            }
        }

        _attached = true;
        UpdateSnapType();
    }

    private void UnloadContent(ButtonBase button)
    {
        if (!_attached) return;

        _attached = false;

        button.LayoutUpdated -= AssociatedObject_LayoutUpdated;

        button.UnregisterPropertyChangedCallback(Control.PaddingProperty, _paddingChangedEventToken);
        _paddingChangedEventToken = 0;

        _visualStateGroup?.CurrentStateChanging -= VisualStateGroup_CurrentStateChanging;
        _visualStateGroup = null;

        if (_contentPresenter != null)
        {
            _contentPresenter.UnregisterPropertyChangedCallback(ContentPresenter.ContentProperty,
                _contentChangedEventToken);
            _contentChangedEventToken = 0;
            _contentPresenter = null;
        }

        _contentVisual?.StopAnimation("Translation");
        _contentVisual = null;

        _propSet.InsertVector3("Offset", Vector3.Zero);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.Loaded += AssociatedObject_Loaded;
        AssociatedObject.Unloaded += AssociatedObject_Unloaded;

        TryLoadContent(AssociatedObject);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        AssociatedObject.Loaded -= AssociatedObject_Loaded;
        AssociatedObject.Unloaded -= AssociatedObject_Unloaded;

        UnloadContent(AssociatedObject);
    }

    private void OnPaddingPropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        UpdateSnapType();
    }

    private void OnContentChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (_attached && _contentPresenter != null)
        {
            _contentVisual?.StopAnimation("Translation");
            _contentVisual = null;

            var actualContent = VisualTreeHelper.GetChild(_contentPresenter, 0)?.As<UIElement>();

            if (actualContent != null)
            {
                _contentVisual = ElementCompositionPreview.GetElementVisual(actualContent);
                if (Environment.OSVersion.Version >= _supportedVersion)
#pragma warning disable CA1416 // 验证平台兼容性
                    _contentVisual.IsPixelSnappingEnabled = true;
#pragma warning restore CA1416 // 验证平台兼容性
                ElementCompositionPreview.SetIsTranslationEnabled(actualContent, true);
            }
        }
    }

    private void VisualStateGroup_CurrentStateChanging(object sender, VisualStateChangedEventArgs e)
    {
        if (!_attached || _contentVisual == null) return;

        if (e.NewState?.Name == "PointerOver" || e.NewState?.Name == "Pressed")
            _contentVisual.StartAnimation("Translation", _translationAnimation1);
        else
            _contentVisual.StartAnimation("Translation", _translationAnimation2);
    }


    private void UpdateSnapType()
    {
        var button = AssociatedObject;
        if (button == null) return;

        if (_attached)
        {
            var hover = false;

            if (_visualStateGroup != null)
                hover = _visualStateGroup.CurrentState?.Name == "PointerOver"
                        || _visualStateGroup.CurrentState?.Name == "Pressed";

            var padding = button.Padding;

            var offset = SnapType switch
            {
                ButtonContentSnapType.Left => new Vector3((float)-padding.Left, 0, 0),
                ButtonContentSnapType.Top => new Vector3(0, (float)-padding.Top, 0),
                ButtonContentSnapType.Right => new Vector3((float)padding.Right, 0, 0),
                ButtonContentSnapType.Bottom => new Vector3(0, (float)padding.Bottom, 0),
                _ => Vector3.Zero
            };

            _propSet.InsertVector3("Offset", offset);

            if (_contentVisual != null)
            {
                _contentVisual.StopAnimation("Translation");

                if (hover)
                    _contentVisual.Properties.InsertVector3("Translation", Vector3.Zero);
                else
                    _contentVisual.Properties.InsertVector3("Translation", offset);
            }
        }
    }

    private static T? FindChild<T>(DependencyObject obj) where T : DependencyObject
    {
        if (obj == null) return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T value) return value;

            var value2 = FindChild<T>(child);
            if (value2 != null) return value2;
        }

        return null;
    }
}

public enum ButtonContentSnapType
{
    None,
    Left,
    Top,
    Right,
    Bottom
}