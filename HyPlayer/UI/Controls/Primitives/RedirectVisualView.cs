using System;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using WinRT;

namespace HyPlayer.UI.Controls.Primitives;

[ContentProperty(Name = nameof(Child))]
public partial class RedirectVisualView : Control
{
    private const string ChildHostTemplatePartName = "ChildHost";

    public RedirectVisualView()
    {
        DefaultStyleKey = typeof(RedirectVisualView);
        DefaultStyleResourceUri = new Uri("ms-appx:///Themes/Generic.xaml");

        _childVisualBrushOffsetEnabled = ChildVisualBrushOffsetEnabled;

        _hostVisual = ElementCompositionPreview.GetElementVisual(this);
        _compositor = _hostVisual.Compositor;

        _childVisualSurface = _compositor.CreateVisualSurface();
        _childVisualBrush = _compositor.CreateSurfaceBrush(_childVisualSurface);
        _childVisualBrush.HorizontalAlignmentRatio = 0;
        _childVisualBrush.VerticalAlignmentRatio = 0;
        _childVisualBrush.Stretch = CompositionStretch.None;

        RootVisual = _compositor.CreateSpriteVisual();
        RootVisual.RelativeSizeAdjustment = Vector2.One;
        RootVisual.Brush = _childVisualBrush;
        if (Environment.OSVersion.Version >= _supportedVersion)
        {
#pragma warning disable CA1416 // 验证平台兼容性
            RootVisual.IsPixelSnappingEnabled = UseLayoutRounding;
#pragma warning restore CA1416 // 验证平台兼容性
        }

        if (_childVisualBrushOffsetEnabled)
            _offsetBind = _compositor.CreateExpressionAnimation("Vector2(visual.Offset.X, visual.Offset.Y)");

        Loaded += RedirectVisualView_Loaded;
        Unloaded += RedirectVisualView_Unloaded;
        RegisterPropertyChangedCallback(PaddingProperty, OnPaddingPropertyChanged);
        RegisterPropertyChangedCallback(UseLayoutRoundingProperty, OnUseLayoutRoundingPropertyChanged);
    }

    protected virtual bool ChildVisualBrushOffsetEnabled => true;

    private readonly Version _supportedVersion = new(10, 0, 20348, 0);

    private bool _measureChildInBoundingBox = true;

    protected bool MeasureChildInBoundingBox
    {
        get => _measureChildInBoundingBox;
        set
        {
            if (_measureChildInBoundingBox != value)
            {
                _measureChildInBoundingBox = value;
                UpdateMeasureChildInBoundingBox();
            }
        }
    }

    protected bool RedirectVisualAttached { get; private set; }

    protected bool RedirectVisualEnabled
    {
        get => _redirectVisualEnabled;
        set
        {
            if (_redirectVisualEnabled != value)
            {
                _redirectVisualEnabled = value;

                if (value)
                {
                    if (IsLoaded) AttachVisuals();
                }
                else
                {
                    DetachVisuals();
                }
            }
        }
    }


    private bool _redirectVisualEnabled = true;
    private readonly bool _childVisualBrushOffsetEnabled;
#nullable enable
    private Grid? _layoutRoot;
    private ContentPresenter? _childPresenter;
    private Grid? _childPresenterContainer;
    private Canvas? _childHost;


    protected Grid? LayoutRoot
    {
        get => _layoutRoot;
        private set
        {
            if (_layoutRoot != value)
            {
                var old = _layoutRoot;

                _layoutRoot = value;

                old?.SizeChanged -= LayoutRoot_SizeChanged;

                _layoutRoot?.SizeChanged += LayoutRoot_SizeChanged;
            }
        }
    }

    protected ContentPresenter? ChildPresenter
    {
        get => _childPresenter;
        private set
        {
            if (_childPresenter != value)
            {
                var old = _childPresenter;

                _childPresenter = value;

                old?.SizeChanged -= ChildPresenter_SizeChanged;

                _childPresenter?.SizeChanged += ChildPresenter_SizeChanged;
            }
        }
    }

    protected Grid? ChildPresenterContainer
    {
        get => _childPresenterContainer;
        private set
        {
            if (_childPresenterContainer != value)
            {
                _childPresenterContainer = value;

                UpdateMeasureChildInBoundingBox();
            }
        }
    }


    protected Canvas? OpacityMaskContainer { get; private set; }

    private readonly Visual _hostVisual;
    private readonly Compositor _compositor;

    private readonly CompositionVisualSurface _childVisualSurface;
    private readonly CompositionSurfaceBrush _childVisualBrush;

    private readonly ExpressionAnimation? _offsetBind;

    protected CompositionBrush ChildVisualBrush => _childVisualBrush;

    protected SpriteVisual RootVisual { get; set; }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DetachVisuals();

        LayoutRoot = GetTemplateChild(nameof(LayoutRoot))?.As<Grid>();
        ChildPresenter = GetTemplateChild(nameof(ChildPresenter))?.As<ContentPresenter>();
        ChildPresenterContainer = GetTemplateChild(nameof(ChildPresenterContainer))?.As<Grid>();
        _childHost = GetTemplateChild(ChildHostTemplatePartName)?.As<Canvas>();
        OpacityMaskContainer = GetTemplateChild(nameof(OpacityMaskContainer))?.As<Canvas>();

        if (RedirectVisualEnabled) AttachVisuals();
    }

    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register("Child", typeof(UIElement), typeof(RedirectVisualView), new PropertyMetadata(null));
#nullable restore
    private void AttachVisuals()
    {
        if (RedirectVisualAttached) return;

        RedirectVisualAttached = true;

        if (LayoutRoot != null)
        {
            if (ChildPresenter != null)
            {
                var childBorderVisual = ElementCompositionPreview.GetElementVisual(ChildPresenter);

                _childVisualSurface.SourceVisual = childBorderVisual;

                if (_offsetBind != null)
                {
                    _offsetBind.SetReferenceParameter("visual", childBorderVisual);
                    _childVisualBrush.StartAnimation("Offset", _offsetBind);
                }
            }

            if (ChildPresenterContainer != null)
                ElementCompositionPreview.GetElementVisual(ChildPresenterContainer).IsVisible = false;

            if (OpacityMaskContainer != null)
                ElementCompositionPreview.GetElementVisual(OpacityMaskContainer).IsVisible = false;

            if (_childHost != null) ElementCompositionPreview.SetElementChildVisual(_childHost, RootVisual);

            UpdateSize();
        }

        OnAttachVisuals();
    }

    private void DetachVisuals()
    {
        if (!RedirectVisualAttached) return;

        RedirectVisualAttached = false;

        if (LayoutRoot != null)
        {
            _childVisualSurface.SourceVisual = null;

            if (_offsetBind != null)
            {
                _childVisualBrush.StopAnimation("Offset");
                _offsetBind.ClearAllParameters();
            }

            if (ChildPresenterContainer != null)
                ElementCompositionPreview.GetElementVisual(ChildPresenterContainer).IsVisible = true;

            if (OpacityMaskContainer != null)
                ElementCompositionPreview.GetElementVisual(OpacityMaskContainer).IsVisible = true;

            if (_childHost != null) ElementCompositionPreview.SetElementChildVisual(_childHost, null);
        }

        OnDetachVisuals();
    }

    private void RedirectVisualView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachVisuals();
        LayoutRoot = null;
        ChildPresenter = null;
        ChildPresenterContainer = null;
        _childHost = null;
        OpacityMaskContainer = null;
    }

    private void RedirectVisualView_Loaded(object sender, RoutedEventArgs e)
    {
        if (RedirectVisualEnabled) AttachVisuals();
    }

    private void OnPaddingPropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        UpdateSize();
    }

    private void OnUseLayoutRoundingPropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        ((RedirectVisualView)sender).OnUseLayoutRoundingChanged();
    }

    protected virtual void OnUseLayoutRoundingChanged()
    {
        if (Environment.OSVersion.Version >= _supportedVersion)
#pragma warning disable CA1416 // 验证平台兼容性
            RootVisual.IsPixelSnappingEnabled = UseLayoutRounding;
#pragma warning restore CA1416 // 验证平台兼容性
    }

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSize();
    }


    private void ChildPresenter_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSize();
    }

    private void UpdateSize()
    {
        if (RedirectVisualAttached && LayoutRoot != null)
            if (ChildPresenter != null)
                _childVisualSurface.SourceSize =
                    new Vector2((float)ChildPresenter.ActualWidth, (float)ChildPresenter.ActualHeight);

        OnUpdateSize();
    }

    private void UpdateMeasureChildInBoundingBox()
    {
        if (ChildPresenterContainer != null)
        {
            var value = MeasureChildInBoundingBox;

            var length = new GridLength(1, value ? GridUnitType.Star : GridUnitType.Auto);

            if (ChildPresenterContainer.RowDefinitions.Count > 0)
                ChildPresenterContainer.RowDefinitions[0].Height = length;
            if (ChildPresenterContainer.ColumnDefinitions.Count > 0)
                ChildPresenterContainer.ColumnDefinitions[0].Width = length;
        }
    }

    protected virtual void OnAttachVisuals()
    {
    }

    protected virtual void OnDetachVisuals()
    {
    }

    protected virtual void OnUpdateSize()
    {
    }
}
