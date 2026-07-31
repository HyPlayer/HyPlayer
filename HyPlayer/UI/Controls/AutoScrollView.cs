using System;
using Windows.ApplicationModel.Core;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;
using HyPlayer.UI.Controls.Primitives;

namespace HyPlayer.UI.Controls;

/// <summary>
///     Marquee effect for UIElement
/// </summary>
public partial class AutoScrollView : RedirectVisualView
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register("Spacing", typeof(double), typeof(AutoScrollView), new PropertyMetadata(20d,
            (s, a) =>
            {
                if (s is AutoScrollView sender && !Equals(a.NewValue, a.OldValue))
                {
                    var value = Convert.ToSingle(a.NewValue);
                    if (value < 0) throw new ArgumentException(nameof(Spacing));

                    sender._propSet.InsertScalar(nameof(Spacing), value);
                }
            }));


    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register("IsPlaying", typeof(bool), typeof(AutoScrollView), new PropertyMetadata(true,
            (s, a) =>
            {
                if (s is AutoScrollView sender && !Equals(a.NewValue, a.OldValue)) sender.UpdateAnimationState();
            }));


    public static readonly DependencyProperty ScrollingPixelsPreSecondProperty =
        DependencyProperty.Register("ScrollingPixelsPreSecond", typeof(double), typeof(AutoScrollView),
            new PropertyMetadata(30d, (s, a) =>
            {
                if (s is AutoScrollView sender && !Equals(a.NewValue, a.OldValue))
                {
                    var value = Convert.ToSingle(a.NewValue);
                    if (value <= 0) throw new ArgumentException(nameof(ScrollingPixelsPreSecondProperty));

                    sender.UpdateAnimationSpeed();
                }
            }));

    private readonly ScalarKeyFrameAnimation _animation;

    private readonly Compositor _compositor;

    private readonly LinearEasingFunction _linearEasingFunc;

    private readonly ExpressionAnimation _offsetBind1;
    private readonly ExpressionAnimation _offsetBind2;

    private readonly CompositionPropertySet _propSet;
    private readonly ExpressionAnimation _sizeBind;

    private readonly SpriteVisual _visual1;
    private readonly SpriteVisual _visual2;

    public AutoScrollView()
    {
        RedirectVisualEnabled = false;

        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

        _propSet = _compositor.CreatePropertySet();
        _propSet.InsertScalar(nameof(Spacing), (float)Spacing);

        _visual1 = _compositor.CreateSpriteVisual();
        _visual1.Brush = ChildVisualBrush;

        _visual2 = _compositor.CreateSpriteVisual();
        _visual2.Brush = ChildVisualBrush;

        _offsetBind1 = _compositor.CreateExpressionAnimation("Vector3(visual.Offset.X, visual.Offset.Y, 0)");
        _offsetBind2 =
            _compositor.CreateExpressionAnimation(
                "Vector3(visual.Offset.X + visual.Size.X + propSet.Spacing, visual.Offset.Y, 0)");

        _offsetBind2.SetReferenceParameter("propSet", _propSet);

        _sizeBind = _compositor.CreateExpressionAnimation("visual.Size");

        RootVisual.Brush = null;
        RootVisual.Children.InsertAtTop(_visual2);
        RootVisual.Children.InsertAtTop(_visual1);

        _linearEasingFunc = _compositor.CreateLinearEasingFunction();

        _animation = _compositor.CreateScalarKeyFrameAnimation();
        _animation.InsertKeyFrame(0, 0);
        _animation.InsertExpressionKeyFrame(1, "-visual.Size.X - propSet.Spacing", _linearEasingFunc);
        _animation.Duration = TimeSpan.FromSeconds(1);
        _animation.IterationBehavior = AnimationIterationBehavior.Forever;
        _animation.SetReferenceParameter("propSet", _propSet);

        MeasureChildInBoundingBox = IsPlaying;

        Loaded += AutoScrollView_Loaded;
        Unloaded += OnUnloaded;
    }

    protected override bool ChildVisualBrushOffsetEnabled => false;

    /// <summary>
    ///     Space between each element. Default value is 20.
    /// </summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    ///     Play the animation when IsPlaying is true. Default value is true.
    /// </summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>
    ///     The pixels of the animation scroll per second. Default value is 30.
    /// </summary>
    public double ScrollingPixelsPreSecond
    {
        get => (double)GetValue(ScrollingPixelsPreSecondProperty);
        set => SetValue(ScrollingPixelsPreSecondProperty, value);
    }


    private void AutoScrollView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAnimationState();
    }


    protected override void OnAttachVisuals()
    {
        base.OnAttachVisuals();

        if (ChildPresenter != null && LayoutRoot != null)
        {
            var childVisual = ElementCompositionPreview.GetElementVisual(ChildPresenter);
            var rootVisual = ElementCompositionPreview.GetElementVisual(LayoutRoot);

            rootVisual.Clip = _compositor.CreateInsetClip();

            _offsetBind1.SetReferenceParameter("visual", childVisual);
            _offsetBind2.SetReferenceParameter("visual", childVisual);

            _offsetBind2.SetReferenceParameter("propSet", _propSet);

            _sizeBind.SetReferenceParameter("visual", childVisual);

            _animation.SetReferenceParameter("visual", childVisual);
            _animation.SetReferenceParameter("visual2", rootVisual);
            _animation.SetReferenceParameter("propSet", _propSet);
            _animation.Duration = TimeSpan.FromSeconds(ChildPresenter.ActualWidth / ScrollingPixelsPreSecond);

            _visual1.StartAnimation("Offset", _offsetBind1);
            _visual1.StartAnimation("Size", _sizeBind);
            _visual2.StartAnimation("Offset", _offsetBind2);
            _visual2.StartAnimation("Size", _sizeBind);

            RootVisual.StartAnimation("Offset.X", _animation);
        }
    }

    protected override void OnDetachVisuals()
    {
        base.OnDetachVisuals();

        _visual1.StopAnimation("Offset");
        _visual1.StopAnimation("Size");
        _visual2.StopAnimation("Offset");
        _visual2.StopAnimation("Size");

        RootVisual.StopAnimation("Offset.X");

        _offsetBind1.ClearAllParameters();
        _offsetBind2.ClearAllParameters();
        _sizeBind.ClearAllParameters();
        _animation.ClearAllParameters();
    }

    protected override void OnUpdateSize()
    {
        base.OnUpdateSize();

        CoreApplication.MainView.DispatcherQueue.TryEnqueue(UpdateAnimationState);
    }

    private void UpdateAnimationState()
    {
        MeasureChildInBoundingBox = !IsPlaying;

        if (IsLoaded
            && IsPlaying
            && ChildPresenter != null
            && LayoutRoot != null)
        {
            var childWidth = ChildPresenter.ActualWidth;
            var rootWidth = LayoutRoot.ActualWidth - Padding.Left - Padding.Right;

            if (childWidth > rootWidth)
                RedirectVisualEnabled = true;
            else
                RedirectVisualEnabled = false;
        }
        else
        {
            RedirectVisualEnabled = false;
        }
    }

    private void UpdateAnimationSpeed()
    {
        if (RedirectVisualAttached && ChildPresenter != null)
        {
            var progress = 0f;
            var animationController = RootVisual.TryGetAnimationController("Offset.X");
            if (animationController != null)
            {
                animationController.Pause();
                progress = animationController.Progress;
            }

            RootVisual.StopAnimation("Offset.X");

            _animation.Duration = TimeSpan.FromSeconds(ChildPresenter.ActualWidth / ScrollingPixelsPreSecond);
            RootVisual.StartAnimation("Offset.X", _animation);

            if (progress > 0)
            {
                animationController = RootVisual.TryGetAnimationController("Offset.X");
                if (animationController != null)
                {
                    animationController.Pause();
                    animationController.Progress = progress;
                    animationController.Resume();
                }
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AutoScrollView_Loaded;
        Unloaded -= OnUnloaded;

        RootVisual.StopAnimation("Offset.X");
        _visual1.StopAnimation("Offset");
        _visual1.StopAnimation("Size");
        _visual2.StopAnimation("Offset");
        _visual2.StopAnimation("Size");

        RootVisual.Children.Remove(_visual2);
        RootVisual.Children.Remove(_visual1);

        _visual2.Dispose();
        _visual1.Dispose();

        _offsetBind1.Dispose();
        _offsetBind2.Dispose();
        _sizeBind.Dispose();
        _animation.Dispose();
        _linearEasingFunc.Dispose();
        _propSet.Dispose();
    }
}