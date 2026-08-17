using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;

namespace HyPlayer.UI.Controls;

[ContentProperty(Name = nameof(Pivot))]
public sealed partial class PivotView : Control
{
    public static readonly DependencyProperty PivotProperty = DependencyProperty.Register(
        nameof(Pivot), typeof(PivotEx), typeof(PivotView), new PropertyMetadata(null, OnPivotChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(UIElement), typeof(PivotView), new PropertyMetadata(null));

    public static readonly DependencyProperty MaxHeaderScrollOffsetProperty = DependencyProperty.Register(
        nameof(MaxHeaderScrollOffset), typeof(double?), typeof(PivotView),
        new PropertyMetadata(null, OnMaxHeaderScrollOffsetChanged));

    public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register(
        nameof(HeaderHeight), typeof(double), typeof(PivotView), new PropertyMetadata(0d));

    public static readonly DependencyProperty HeaderScrollProgressProperty = DependencyProperty.Register(
        nameof(HeaderScrollProgress), typeof(double), typeof(PivotView), new PropertyMetadata(0d));

    private Border? _headerContainer;
    private Border? _pivotContainer;
    private PivotExHeaderView? _headerView;
    private ExpressionAnimation? _headerOffsetAnimation;
    private UIElement? _animatedHeaderContainer;
    private UIElement? _animatedHeaderView;
    private bool _areTemplatePartEventsAttached;
    private bool _isPivotEventAttached;

    public PivotView()
    {
        DefaultStyleKey = typeof(PivotView);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PivotEx? Pivot
    {
        get => (PivotEx?)GetValue(PivotProperty);
        set => SetValue(PivotProperty, value);
    }

    public UIElement? Header
    {
        get => (UIElement?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public double? MaxHeaderScrollOffset
    {
        get => (double?)GetValue(MaxHeaderScrollOffsetProperty);
        set => SetValue(MaxHeaderScrollOffsetProperty, value);
    }

    public double HeaderHeight
    {
        get => (double)GetValue(HeaderHeightProperty);
        private set => SetValue(HeaderHeightProperty, value);
    }

    /// <summary>
    /// Gets the collapsed-header progress, from 0 (expanded) to 1 (fully collapsed).
    /// </summary>
    public double HeaderScrollProgress
    {
        get => (double)GetValue(HeaderScrollProgressProperty);
        private set => SetValue(HeaderScrollProgressProperty, value);
    }

    public event EventHandler? HeaderHeightChanged;
    public event EventHandler? HeaderScrollProgressChanged;

    private double EffectiveMaxHeaderScrollOffset =>
        Math.Min(_headerContainer?.ActualHeight ?? 0, MaxHeaderScrollOffset ?? double.MaxValue);

    protected override void OnApplyTemplate()
    {
        StopHeaderAnimations();
        DetachTemplatePartEvents();

        base.OnApplyTemplate();

        _headerContainer = GetTemplateChild("HeaderContainer") as Border;
        _headerView = GetTemplateChild("PivotExHeaderView") as PivotExHeaderView;
        _pivotContainer = GetTemplateChild("PivotContainer") as Border;

        AttachTemplatePartEvents();
        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
        UpdatePivotClip();
        StartHeaderAnimations();
    }

    private static void OnPivotChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PivotView view)
            return;

        view.DetachPivotEvent(args.OldValue as PivotEx);
        view.AttachPivotEvent(args.NewValue as PivotEx);
        view.UpdatePivotMaxHeaderScrollOffset();
        view.UpdateScrollProgress();
        view.StartHeaderAnimations();
    }

    private static void OnMaxHeaderScrollOffsetChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PivotView view)
            return;

        view.UpdatePivotMaxHeaderScrollOffset();
        view.UpdatePivotClip();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachPivotEvent(Pivot);
        AttachTemplatePartEvents();
        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
        UpdateScrollProgress();
        StartHeaderAnimations();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachPivotEvent(Pivot);
        DetachTemplatePartEvents();
        StopHeaderAnimations();

        _headerContainer = null;
        _pivotContainer = null;
        _headerView = null;
        HeaderHeightChanged = null;
        HeaderScrollProgressChanged = null;
        ClearValue(HeaderProperty);
        ClearValue(PivotProperty);
    }

    private void AttachPivotEvent(PivotEx? pivot)
    {
        if (pivot is null || _isPivotEventAttached)
            return;

        pivot.HeaderScrollOffsetChanged += OnPivotHeaderScrollOffsetChanged;
        _isPivotEventAttached = true;
    }

    private void DetachPivotEvent(PivotEx? pivot)
    {
        if (pivot is null || !_isPivotEventAttached)
            return;

        pivot.HeaderScrollOffsetChanged -= OnPivotHeaderScrollOffsetChanged;
        _isPivotEventAttached = false;
    }

    private void AttachTemplatePartEvents()
    {
        if (_areTemplatePartEventsAttached)
            return;

        if (_headerContainer is not null)
            _headerContainer.SizeChanged += OnHeaderContainerSizeChanged;
        if (_headerView is not null)
            _headerView.SizeChanged += OnHeaderViewSizeChanged;
        if (_pivotContainer is not null)
            _pivotContainer.SizeChanged += OnPivotContainerSizeChanged;
        _areTemplatePartEventsAttached = true;
    }

    private void DetachTemplatePartEvents()
    {
        if (!_areTemplatePartEventsAttached)
            return;

        if (_headerContainer is not null)
            _headerContainer.SizeChanged -= OnHeaderContainerSizeChanged;
        if (_headerView is not null)
            _headerView.SizeChanged -= OnHeaderViewSizeChanged;
        if (_pivotContainer is not null)
            _pivotContainer.SizeChanged -= OnPivotContainerSizeChanged;
        _areTemplatePartEventsAttached = false;
    }

    private void UpdateHeaderHeight()
    {
        var height = (_headerContainer?.ActualHeight ?? 0) + (_headerView?.ActualHeight ?? 0);
        if (HeaderHeight.Equals(height))
            return;

        HeaderHeight = height;
        HeaderHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePivotMaxHeaderScrollOffset()
    {
        if (Pivot is null)
            return;

        Pivot.MaxHeaderScrollOffset = EffectiveMaxHeaderScrollOffset;
        UpdateScrollProgress();
    }

    private void UpdateScrollProgress()
    {
        var progress = Pivot is null || Pivot.MaxHeaderScrollOffset <= 0
            ? 0
            : Math.Clamp(Pivot.HeaderScrollOffset / Pivot.MaxHeaderScrollOffset, 0, 1);
        if (HeaderScrollProgress.Equals(progress))
            return;

        HeaderScrollProgress = progress;
        HeaderScrollProgressChanged?.Invoke(this, EventArgs.Empty);
        UpdatePivotClip();
    }

    private void UpdatePivotClip()
    {
        if (_pivotContainer is null)
            return;

        if (_pivotContainer.Clip is not RectangleGeometry clip)
        {
            clip = new RectangleGeometry();
            _pivotContainer.Clip = clip;
        }

        var y = HeaderScrollProgress > 0.99
            ? (_headerView?.ActualHeight ?? 0) + (_headerContainer?.ActualHeight ?? 0) -
              EffectiveMaxHeaderScrollOffset
            : 0;
        clip.Rect = new Rect(0, y, _pivotContainer.ActualWidth,
            Math.Max(0, _pivotContainer.ActualHeight - y));
    }

    private void StartHeaderAnimations()
    {
        StopHeaderAnimations();
        if (Pivot is null)
            return;

        var progressPropertySet = Pivot.GetProgressPropertySet();
        _headerOffsetAnimation = progressPropertySet.Compositor.CreateExpressionAnimation(
            "Vector3(0, -prop.OffsetY, 0)");
        _headerOffsetAnimation.SetReferenceParameter("prop", progressPropertySet);

        StartTranslationAnimation(_headerContainer);
        _animatedHeaderContainer = _headerContainer;
        StartTranslationAnimation(_headerView);
        _animatedHeaderView = _headerView;
    }

    private void StartTranslationAnimation(UIElement? element)
    {
        if (element is null || _headerOffsetAnimation is null)
            return;

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.StartAnimation("Translation", _headerOffsetAnimation);
    }

    private void StopHeaderAnimations()
    {
        StopTranslationAnimation(_animatedHeaderContainer);
        StopTranslationAnimation(_animatedHeaderView);
        _animatedHeaderContainer = null;
        _animatedHeaderView = null;
        _headerOffsetAnimation?.Dispose();
        _headerOffsetAnimation = null;
    }

    private static void StopTranslationAnimation(UIElement? element)
    {
        if (element is null)
            return;

        ElementCompositionPreview.GetElementVisual(element).StopAnimation("Translation");
        ElementCompositionPreview.SetIsTranslationEnabled(element, false);
    }

    private void OnHeaderViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
        UpdatePivotClip();
    }

    private void OnHeaderContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
    }

    private void OnPivotHeaderScrollOffsetChanged(object? sender, EventArgs e) => UpdateScrollProgress();

    private void OnPivotContainerSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePivotClip();
}
