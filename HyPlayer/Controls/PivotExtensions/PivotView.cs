using System;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;

namespace HyPlayer.Controls;

[ContentProperty(Name = "Pivot")]
internal class PivotView : Control
{
    public static readonly DependencyProperty PivotProperty =
        DependencyProperty.Register("Pivot", typeof(PivotEx), typeof(PivotView), new PropertyMetadata(null, (s, a) =>
        {
            if (s is PivotView sender)
            {
                if (a.OldValue is PivotEx oldValue)
                    oldValue.HeaderScrollOffsetChanged -= sender.Pivot_HeaderScrollOffsetChanged;

                if (a.NewValue is PivotEx newValue)
                    newValue.HeaderScrollOffsetChanged += sender.Pivot_HeaderScrollOffsetChanged;

                sender.UpdatePivotMaxHeaderScrollOffset();
                sender.UpdateProgressPropertySet();
            }
        }));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register("Header", typeof(UIElement), typeof(PivotView), new PropertyMetadata(null));

    public static readonly DependencyProperty MaxHeaderScrollOffsetProperty =
        DependencyProperty.Register("MaxHeaderScrollOffset", typeof(double?), typeof(PivotView), new PropertyMetadata(
            null, (s, a) =>
            {
                if (s is PivotView sender)
                {
                    sender.UpdatePivotMaxHeaderScrollOffset();
                    sender.UpdatePivotClip();
                }
            }));


    public static readonly DependencyProperty HeaderHeightProperty =
        DependencyProperty.Register("HeaderHeight", typeof(double), typeof(PivotView), new PropertyMetadata(0d,
            (s, a) =>
            {
                if (s is PivotView sender)
                {
                    if (!sender.internalSet) throw new ArgumentException(nameof(HeaderHeight));
                    sender.HeaderHeightChanged?.Invoke(sender, EventArgs.Empty);
                }
            }));


    public static readonly DependencyProperty HeaderScrollProgressProperty =
        DependencyProperty.Register("HeaderScrollProgress", typeof(double), typeof(PivotView), new PropertyMetadata(0d,
            (s, a) =>
            {
                if (s is PivotView sender)
                {
                    if (!sender.internalSet) throw new ArgumentException(nameof(HeaderScrollProgress));
                    sender.HeaderScrollProgressChanged?.Invoke(sender, EventArgs.Empty);
                }
            }));


    private Border HeaderContainer;
    private bool internalSet;
    private ExpressionAnimation offsetBind;
    private Border PivotContainer;
    private PivotExHeaderView PivotExHeaderView;

    private CompositionPropertySet progressPropSet;

    public PivotView()
    {
        DefaultStyleKey = typeof(PivotView);
    }

    public PivotEx Pivot
    {
        get => (PivotEx)GetValue(PivotProperty);
        set => SetValue(PivotProperty, value);
    }

    public UIElement Header
    {
        get => (UIElement)GetValue(HeaderProperty);
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
    ///     未滚动时为0，滚动到极限时为1
    /// </summary>
    public double HeaderScrollProgress
    {
        get => (double)GetValue(HeaderScrollProgressProperty);
        private set => SetValue(HeaderScrollProgressProperty, value);
    }

    private double InternalMaxHeaderScrollOffset =>
        Math.Min(HeaderContainer?.ActualHeight ?? 0, MaxHeaderScrollOffset ?? double.MaxValue);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (HeaderContainer != null) HeaderContainer.SizeChanged -= HeaderContainer_SizeChanged;
        if (PivotExHeaderView != null) PivotExHeaderView.SizeChanged -= PivotExHeaderView_SizeChanged;
        if (PivotContainer != null) PivotContainer.SizeChanged += PivotContainer_SizeChanged;

        HeaderContainer = (Border)GetTemplateChild(nameof(HeaderContainer));
        PivotExHeaderView = (PivotExHeaderView)GetTemplateChild(nameof(PivotExHeaderView));
        PivotContainer = (Border)GetTemplateChild(nameof(PivotContainer));

        if (HeaderContainer != null) HeaderContainer.SizeChanged += HeaderContainer_SizeChanged;
        if (PivotExHeaderView != null) PivotExHeaderView.SizeChanged += PivotExHeaderView_SizeChanged;
        if (PivotContainer != null) PivotContainer.SizeChanged += PivotContainer_SizeChanged;

        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
        UpdatePivotClip();
        UpdateProgressPropertySet();
    }

    private void UpdateHeaderHeight()
    {
        internalSet = true;
        try
        {
            HeaderHeight = (HeaderContainer?.ActualHeight ?? 0) + (PivotExHeaderView?.ActualHeight ?? 0);
        }
        finally
        {
            internalSet = false;
        }
    }

    private void UpdatePivotMaxHeaderScrollOffset()
    {
        if (Pivot == null) return;
        Pivot.MaxHeaderScrollOffset = InternalMaxHeaderScrollOffset;

        UpdateScrollProgress();
    }

    private void UpdateScrollProgress()
    {
        if (Pivot == null || Pivot.MaxHeaderScrollOffset == 0) return;

        var progress = Pivot.HeaderScrollOffset / Pivot.MaxHeaderScrollOffset;

        internalSet = true;
        try
        {
            HeaderScrollProgress = progress;
        }
        finally
        {
            internalSet = false;
        }

        UpdatePivotClip();
    }

    private void UpdatePivotClip()
    {
        if (PivotContainer != null)
        {
            if (PivotContainer.Clip is not RectangleGeometry clip)
            {
                clip = new RectangleGeometry();
                PivotContainer.Clip = clip;
            }

            if (HeaderScrollProgress > 0.99)
            {
                var y = (PivotExHeaderView?.ActualHeight ?? 0) + (HeaderContainer?.ActualHeight ?? 0) -
                        InternalMaxHeaderScrollOffset;
                clip.Rect = new Rect(0, y, PivotContainer.ActualWidth, Math.Max(0, PivotContainer.ActualHeight - y));
            }
            else
            {
                clip.Rect = new Rect(0, 0, PivotContainer.ActualWidth, PivotContainer.ActualHeight);
            }
        }
    }

    private void UpdateProgressPropertySet()
    {
        if (Pivot != null)
        {
            progressPropSet = Pivot.GetProgressPropertySet();
            offsetBind = progressPropSet.Compositor.CreateExpressionAnimation("Vector3(0, -prop.OffsetY, 0)");
            offsetBind.SetReferenceParameter("prop", progressPropSet);

            if (HeaderContainer != null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(HeaderContainer, true);
                var visual = ElementCompositionPreview.GetElementVisual(HeaderContainer);
                visual.StartAnimation("Translation", offsetBind);
            }

            if (PivotExHeaderView != null)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(PivotExHeaderView, true);
                var visual = ElementCompositionPreview.GetElementVisual(PivotExHeaderView);
                visual.StartAnimation("Translation", offsetBind);
            }
        }
        else
        {
            progressPropSet = null;
            offsetBind = null;
            if (HeaderContainer != null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(HeaderContainer);
                visual.StopAnimation("Translation");
            }

            if (PivotExHeaderView != null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(PivotExHeaderView);
                visual.StopAnimation("Translation");
            }
        }
    }

    private void PivotExHeaderView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHeaderHeight();
        UpdatePivotMaxHeaderScrollOffset();
        UpdatePivotClip();
    }

    private void HeaderContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHeaderHeight();
    }

    private void Pivot_HeaderScrollOffsetChanged(object sender, EventArgs e)
    {
        UpdatePivotMaxHeaderScrollOffset();
        UpdateScrollProgress();
    }


    private void PivotContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePivotClip();
    }


    public event EventHandler HeaderHeightChanged;
    public event EventHandler HeaderScrollProgressChanged;
}