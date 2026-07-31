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

                    sender.propSet.InsertScalar(nameof(Spacing), value);
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

    private readonly ScalarKeyFrameAnimation animation;

    private readonly Compositor compositor;

    private readonly LinearEasingFunction linearEasingFunc;

    private readonly ExpressionAnimation offsetBind1;
    private readonly ExpressionAnimation offsetBind2;

    private readonly CompositionPropertySet propSet;
    private readonly ExpressionAnimation sizeBind;

    private readonly SpriteVisual visual1;
    private readonly SpriteVisual visual2;

    public AutoScrollView()
    {
        RedirectVisualEnabled = false;

        compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

        propSet = compositor.CreatePropertySet();
        propSet.InsertScalar(nameof(Spacing), (float)Spacing);

        visual1 = compositor.CreateSpriteVisual();
        visual1.Brush = ChildVisualBrush;

        visual2 = compositor.CreateSpriteVisual();
        visual2.Brush = ChildVisualBrush;

        offsetBind1 = compositor.CreateExpressionAnimation("Vector3(visual.Offset.X, visual.Offset.Y, 0)");
        offsetBind2 =
            compositor.CreateExpressionAnimation(
                "Vector3(visual.Offset.X + visual.Size.X + propSet.Spacing, visual.Offset.Y, 0)");

        offsetBind2.SetReferenceParameter("propSet", propSet);

        sizeBind = compositor.CreateExpressionAnimation("visual.Size");

        RootVisual.Brush = null;
        RootVisual.Children.InsertAtTop(visual2);
        RootVisual.Children.InsertAtTop(visual1);

        linearEasingFunc = compositor.CreateLinearEasingFunction();

        animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0);
        animation.InsertExpressionKeyFrame(1, "-visual.Size.X - propSet.Spacing", linearEasingFunc);
        animation.Duration = TimeSpan.FromSeconds(1);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.SetReferenceParameter("propSet", propSet);

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

            rootVisual.Clip = compositor.CreateInsetClip();

            offsetBind1.SetReferenceParameter("visual", childVisual);
            offsetBind2.SetReferenceParameter("visual", childVisual);

            offsetBind2.SetReferenceParameter("propSet", propSet);

            sizeBind.SetReferenceParameter("visual", childVisual);

            animation.SetReferenceParameter("visual", childVisual);
            animation.SetReferenceParameter("visual2", rootVisual);
            animation.SetReferenceParameter("propSet", propSet);
            animation.Duration = TimeSpan.FromSeconds(ChildPresenter.ActualWidth / ScrollingPixelsPreSecond);

            visual1.StartAnimation("Offset", offsetBind1);
            visual1.StartAnimation("Size", sizeBind);
            visual2.StartAnimation("Offset", offsetBind2);
            visual2.StartAnimation("Size", sizeBind);

            RootVisual.StartAnimation("Offset.X", animation);
        }
    }

    protected override void OnDetachVisuals()
    {
        base.OnDetachVisuals();

        visual1.StopAnimation("Offset");
        visual1.StopAnimation("Size");
        visual2.StopAnimation("Offset");
        visual2.StopAnimation("Size");

        RootVisual.StopAnimation("Offset.X");

        offsetBind1.ClearAllParameters();
        offsetBind2.ClearAllParameters();
        sizeBind.ClearAllParameters();
        animation.ClearAllParameters();
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

            animation.Duration = TimeSpan.FromSeconds(ChildPresenter.ActualWidth / ScrollingPixelsPreSecond);
            RootVisual.StartAnimation("Offset.X", animation);

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
        visual1.StopAnimation("Offset");
        visual1.StopAnimation("Size");
        visual2.StopAnimation("Offset");
        visual2.StopAnimation("Size");

        RootVisual.Children.Remove(visual2);
        RootVisual.Children.Remove(visual1);

        visual2.Dispose();
        visual1.Dispose();

        offsetBind1.Dispose();
        offsetBind2.Dispose();
        sizeBind.Dispose();
        animation.Dispose();
        linearEasingFunc.Dispose();
        propSet.Dispose();
    }
}