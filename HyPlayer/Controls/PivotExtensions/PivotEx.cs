using Microsoft.Toolkit.Uwp.UI;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace HyPlayer.Controls;

public class PivotEx : Pivot
{
    public static readonly DependencyProperty MaxHeaderScrollOffsetProperty =
        DependencyProperty.Register("MaxHeaderScrollOffset", typeof(double), typeof(PivotEx), new PropertyMetadata(0d,
            (s, a) =>
            {
                if (s is PivotEx sender)
                {
                    sender.internalPropSet.InsertScalar("MaxHeaderScrollOffset", Convert.ToSingle(a.NewValue));
                    sender.UpdateHeaderScrollOffset();
                    sender.UpdateInternalProgress();
                }
            }));

    public static readonly DependencyProperty HeaderScrollOffsetProperty =
        DependencyProperty.Register("HeaderScrollOffset", typeof(double), typeof(PivotEx), new PropertyMetadata(0d,
            (s, a) =>
            {
                if (s is PivotEx sender)
                {
                    if (!sender.innerSet) throw new ArgumentException(nameof(HeaderScrollOffset));

                    sender.UpdateInternalProgress();
                }
            }));

    private CancellationTokenSource cts;
    private CompositionPropertySet currentScrollPropSet;

    private ScrollViewer currentScrollViewer;
    private bool innerSet;
    private readonly CompositionPropertySet internalPropSet;

    private double lastScrollOffsetY;
    private ExpressionAnimation offsetYBind;
    private readonly CompositionPropertySet progressPropSet;
    private ExpressionAnimation scrollProgressBind;

    public PivotEx()
    {
        DefaultStyleKey = typeof(PivotEx);

        progressPropSet = ElementCompositionPreview.GetElementVisual(this).Compositor.CreatePropertySet();
        progressPropSet.InsertScalar("Progress", 0);
        progressPropSet.InsertScalar("OffsetY", 0);

        internalPropSet = ElementCompositionPreview.GetElementVisual(this).Compositor.CreatePropertySet();
        internalPropSet.InsertScalar("MaxHeaderScrollOffset", 0);

        UpdateInternalProgress();

        SelectionChanged += PivotEx_SelectionChanged;
        Unloaded += PivotEx_Unloaded;
        PivotItemLoaded += PivotEx_PivotItemLoaded;
    }

    public double MaxHeaderScrollOffset
    {
        get => (double)GetValue(MaxHeaderScrollOffsetProperty);
        set => SetValue(MaxHeaderScrollOffsetProperty, value);
    }

    public double HeaderScrollOffset
    {
        get => (double)GetValue(HeaderScrollOffsetProperty);
        private set => SetValue(HeaderScrollOffsetProperty, value);
    }


    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _ = UpdateCurrentScrollViewer();
    }

    private async Task UpdateCurrentScrollViewer()
    {
        var container = ContainerFromIndex(SelectedIndex) as PivotItem;

        var sv = container?.FindDescendant<ScrollViewer>();

        if (sv != null) sv.IsHitTestVisible = true;

        if (sv == currentScrollViewer) return;

        cts?.Cancel();
        cts = null;

        if (currentScrollViewer != null) currentScrollViewer.ViewChanging -= CurrentScrollViewer_ViewChanging;

        currentScrollViewer = sv;

        scrollProgressBind = internalPropSet.Compositor.CreateExpressionAnimation("prop.Progress");
        scrollProgressBind.SetReferenceParameter("prop", internalPropSet);
        offsetYBind = internalPropSet.Compositor.CreateExpressionAnimation("prop.OffsetY");
        offsetYBind.SetReferenceParameter("prop", internalPropSet);

        progressPropSet.StartAnimation("OffsetY", offsetYBind);
        progressPropSet.StartAnimation("Progress", scrollProgressBind);

        currentScrollPropSet = null;

        if (currentScrollViewer != null)
        {
            var _cts = new CancellationTokenSource();
            cts = _cts;

            currentScrollViewer.ViewChanging += CurrentScrollViewer_ViewChanging;

            var offsetY = await TryScrollVerticalOffsetAsync(currentScrollViewer);

            if (cts.IsCancellationRequested) return;

            UpdateHeaderScrollOffset();

            currentScrollPropSet =
                ElementCompositionPreview.GetScrollViewerManipulationPropertySet(currentScrollViewer);

            await Task.Delay(200);

            if (cts.IsCancellationRequested) return;

            offsetYBind =
                currentScrollPropSet.Compositor.CreateExpressionAnimation(
                    "clamp(-scroll.Translation.Y, 0, prop.MaxHeaderScrollOffset)");
            offsetYBind.SetReferenceParameter("scroll", currentScrollPropSet);
            offsetYBind.SetReferenceParameter("prop", internalPropSet);

            progressPropSet.StartAnimation("OffsetY", offsetYBind);

            scrollProgressBind =
                currentScrollPropSet.Compositor.CreateExpressionAnimation(
                    "prop.MaxHeaderScrollOffset == 0 ? 0 : prop2.OffsetY / prop.MaxHeaderScrollOffset");
            scrollProgressBind.SetReferenceParameter("scroll", currentScrollPropSet);
            scrollProgressBind.SetReferenceParameter("prop", internalPropSet);
            scrollProgressBind.SetReferenceParameter("prop2", progressPropSet);

            progressPropSet.StartAnimation("Progress", scrollProgressBind);
        }
    }

    private void CurrentScrollViewer_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
    {
        UpdateHeaderScrollOffset(e.NextView.VerticalOffset);
    }

    private void UpdateHeaderScrollOffset(double? verticalOffset = null)
    {
        innerSet = true;

        var oldValue = HeaderScrollOffset;
        try
        {
            var vt = verticalOffset ?? currentScrollViewer?.VerticalOffset ?? 0;
            lastScrollOffsetY = vt;
            HeaderScrollOffset = Math.Min(MaxHeaderScrollOffset, lastScrollOffsetY);
        }
        finally
        {
            innerSet = false;
        }

        if (oldValue != HeaderScrollOffset) HeaderScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PivotEx_PivotItemLoaded(Pivot sender, PivotItemEventArgs args)
    {
        var sv = args.Item.FindDescendant<ScrollViewer>();
        if (sv != null)
        {
            sv.IsHitTestVisible = false;
            TryScrollVerticalOffsetAsync(sv);
        }

        var container = ContainerFromIndex(SelectedIndex) as PivotItem;
        if (container == args.Item) _ = UpdateCurrentScrollViewer();
    }

    private Task<double?> TryScrollVerticalOffsetAsync(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) return null;

        double? offsetY = null;

        if (lastScrollOffsetY < MaxHeaderScrollOffset)
            offsetY = Math.Min(MaxHeaderScrollOffset, lastScrollOffsetY);
        else if (scrollViewer.VerticalOffset < MaxHeaderScrollOffset) offsetY = MaxHeaderScrollOffset;

        if (offsetY.HasValue)
        {
            if (scrollViewer.ChangeView(null, offsetY.Value, null, true))
            {
                var tcs = new TaskCompletionSource<double?>();
                scrollViewer.ViewChanged += ScrollViewer_ViewChanged;

                return tcs.Task;

                void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
                {
                    scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                    tcs.SetResult(scrollViewer.VerticalOffset);
                }
            }

            scrollViewer.UpdateLayout();
        }

        return Task.FromResult<double?>(null);
    }

    private void UpdateInternalProgress()
    {
        internalPropSet.InsertScalar("Progress",
            (float)(MaxHeaderScrollOffset == 0
                ? 0
                : Math.Clamp(lastScrollOffsetY, 0, MaxHeaderScrollOffset) / MaxHeaderScrollOffset));
        internalPropSet.InsertScalar("OffsetY", (float)Math.Clamp(lastScrollOffsetY, 0, MaxHeaderScrollOffset));
    }

    private void PivotEx_Unloaded(object sender, RoutedEventArgs e)
    {
        lastScrollOffsetY = 0;
    }

    private void PivotEx_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = UpdateCurrentScrollViewer();
    }

    public CompositionPropertySet GetProgressPropertySet()
    {
        return progressPropSet;
    }

    public event EventHandler HeaderScrollOffsetChanged;
}