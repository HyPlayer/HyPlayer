using System;
using System.Numerics;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using WinRT;

namespace HyPlayer.Controls.Primitives;

[ContentProperty(Name = nameof(Child))]
public partial class RedirectVisualView : Control
{
    public RedirectVisualView()
    {
        this.DefaultStyleKey = typeof(RedirectVisualView);
        this.DefaultStyleResourceUri = new Uri("ms-appx:///Themes/Generic.xaml");

        childVisualBrushOffsetEnabled = ChildVisualBrushOffsetEnabled;

        hostVisual = ElementCompositionPreview.GetElementVisual(this);
        compositor = hostVisual.Compositor;

        childVisualSurface = compositor.CreateVisualSurface();
        childVisualBrush = compositor.CreateSurfaceBrush(childVisualSurface);
        childVisualBrush.HorizontalAlignmentRatio = 0;
        childVisualBrush.VerticalAlignmentRatio = 0;
        childVisualBrush.Stretch = CompositionStretch.None;

        redirectVisual = compositor.CreateSpriteVisual();
        redirectVisual.RelativeSizeAdjustment = Vector2.One;
        redirectVisual.Brush = childVisualBrush;
        if (Environment.OSVersion.Version >= SupportedVersion)
        {
            redirectVisual.IsPixelSnappingEnabled = UseLayoutRounding;
        }

        if (childVisualBrushOffsetEnabled)
        {
            offsetBind = compositor.CreateExpressionAnimation("Vector2(visual.Offset.X, visual.Offset.Y)");
        }

        this.Loaded += RedirectVisualView_Loaded;
        this.Unloaded += RedirectVisualView_Unloaded;
        RegisterPropertyChangedCallback(PaddingProperty, OnPaddingPropertyChanged);
        RegisterPropertyChangedCallback(UseLayoutRoundingProperty, OnUseLayoutRoundingPropertyChanged);
    }

    protected virtual bool ChildVisualBrushOffsetEnabled => true;

    private readonly Version SupportedVersion = new Version(10, 0, 20348, 0);

    private bool measureChildInBoundingBox = true;

    protected bool MeasureChildInBoundingBox
    {
        get => measureChildInBoundingBox;
        set
        {
            if (measureChildInBoundingBox != value)
            {
                measureChildInBoundingBox = value;
                UpdateMeasureChildInBoundingBox();
            }
        }
    }

    protected bool RedirectVisualAttached => attached;

    protected bool RedirectVisualEnabled
    {
        get => redirectVisualEnabled;
        set
        {
            if (redirectVisualEnabled != value)
            {
                redirectVisualEnabled = value;

                if (value)
                {
                    if (IsLoaded)
                    {
                        AttachVisuals();
                    }
                }
                else
                {
                    DetachVisuals();
                }
            }
        }
    }


    private bool attached;
    private bool redirectVisualEnabled = true;
    private bool childVisualBrushOffsetEnabled;
#nullable enable
    private Grid? layoutRoot;
    private ContentPresenter? childPresenter;
    private Grid? childPresenterContainer;
    private Canvas? ChildHost;
    private Canvas? opacityMaskContainer;


    protected Grid? LayoutRoot
    {
        get => layoutRoot;
        private set
        {
            if (layoutRoot != value)
            {
                var old = layoutRoot;

                layoutRoot = value;

                if (old != null)
                {
                    old.SizeChanged -= LayoutRoot_SizeChanged;
                }

                if (layoutRoot != null)
                {
                    layoutRoot.SizeChanged += LayoutRoot_SizeChanged;
                }
            }
        }
    }

    protected ContentPresenter? ChildPresenter
    {
        get => childPresenter;
        private set
        {
            if (childPresenter != value)
            {
                var old = childPresenter;

                childPresenter = value;

                if (old != null)
                {
                    old.SizeChanged -= ChildPresenter_SizeChanged;
                }

                if (childPresenter != null)
                {
                    childPresenter.SizeChanged += ChildPresenter_SizeChanged;
                }
            }
        }
    }

    protected Grid? ChildPresenterContainer
    {
        get => childPresenterContainer;
        private set
        {
            if (childPresenterContainer != value)
            {
                childPresenterContainer = value;

                UpdateMeasureChildInBoundingBox();
            }
        }
    }


    protected Canvas? OpacityMaskContainer
    {
        get => opacityMaskContainer;
        private set => opacityMaskContainer = value;
    }

    private Visual hostVisual;
    private Compositor compositor;

    private CompositionVisualSurface childVisualSurface;
    private CompositionSurfaceBrush childVisualBrush;

    private SpriteVisual redirectVisual;
    private ExpressionAnimation? offsetBind;

    protected CompositionBrush ChildVisualBrush => childVisualBrush;

    protected SpriteVisual RootVisual
    {
        get => redirectVisual;
        set => redirectVisual = value;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DetachVisuals();

        LayoutRoot = GetTemplateChild(nameof(LayoutRoot))?.As<Grid>();
        ChildPresenter = GetTemplateChild(nameof(ChildPresenter))?.As<ContentPresenter>();
        ChildPresenterContainer = GetTemplateChild(nameof(ChildPresenterContainer))?.As<Grid>();
        ChildHost = GetTemplateChild(nameof(ChildHost))?.As<Canvas>();
        OpacityMaskContainer = GetTemplateChild(nameof(OpacityMaskContainer))?.As<Canvas>();

        if (RedirectVisualEnabled)
        {
            AttachVisuals();
        }
    }

    public UIElement? Child
    {
        get { return (UIElement?)GetValue(ChildProperty); }
        set { SetValue(ChildProperty, value); }
    }

    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register("Child", typeof(UIElement), typeof(RedirectVisualView), new PropertyMetadata(null));
#nullable restore
    private void AttachVisuals()
    {
        if (attached) return;

        attached = true;

        if (LayoutRoot != null)
        {
            if (ChildPresenter != null)
            {
                var childBorderVisual = ElementCompositionPreview.GetElementVisual(ChildPresenter);

                childVisualSurface.SourceVisual = childBorderVisual;

                if (offsetBind != null)
                {
                    offsetBind.SetReferenceParameter("visual", childBorderVisual);
                    childVisualBrush.StartAnimation("Offset", offsetBind);
                }
            }

            if (ChildPresenterContainer != null)
            {
                ElementCompositionPreview.GetElementVisual(ChildPresenterContainer).IsVisible = false;
            }

            if (OpacityMaskContainer != null)
            {
                ElementCompositionPreview.GetElementVisual(OpacityMaskContainer).IsVisible = false;
            }

            if (ChildHost != null)
            {
                ElementCompositionPreview.SetElementChildVisual(ChildHost, redirectVisual);
            }

            UpdateSize();
        }

        OnAttachVisuals();
    }

    private void DetachVisuals()
    {
        if (!attached) return;

        attached = false;

        if (LayoutRoot != null)
        {
            childVisualSurface.SourceVisual = null;

            if (offsetBind != null)
            {
                childVisualBrush.StopAnimation("Offset");
                offsetBind.ClearAllParameters();
            }

            if (ChildPresenterContainer != null)
            {
                ElementCompositionPreview.GetElementVisual(ChildPresenterContainer).IsVisible = true;
            }

            if (OpacityMaskContainer != null)
            {
                ElementCompositionPreview.GetElementVisual(OpacityMaskContainer).IsVisible = true;
            }

            if (ChildHost != null)
            {
                ElementCompositionPreview.SetElementChildVisual(ChildHost, null);
            }
        }

        OnDetachVisuals();
    }

    private void RedirectVisualView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachVisuals();
    }

    private void RedirectVisualView_Loaded(object sender, RoutedEventArgs e)
    {
        if (RedirectVisualEnabled)
        {
            AttachVisuals();
        }
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
        redirectVisual.IsPixelSnappingEnabled = UseLayoutRounding;
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
        if (attached && LayoutRoot != null)
        {
            if (ChildPresenter != null)
            {
                childVisualSurface.SourceSize = new Vector2((float)ChildPresenter.ActualWidth, (float)ChildPresenter.ActualHeight);
            }
        }

        OnUpdateSize();
    }

    private void UpdateMeasureChildInBoundingBox()
    {
        if (ChildPresenterContainer != null)
        {
            var value = MeasureChildInBoundingBox;

            var length = new GridLength(1, value ? GridUnitType.Star : GridUnitType.Auto);

            if (ChildPresenterContainer.RowDefinitions.Count > 0)
            {
                ChildPresenterContainer.RowDefinitions[0].Height = length;
            }
            if (ChildPresenterContainer.ColumnDefinitions.Count > 0)
            {
                ChildPresenterContainer.ColumnDefinitions[0].Width = length;
            }
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