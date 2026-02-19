using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using WinRT;

namespace HyPlayer.Controls;


public partial class PivotEx : Pivot
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

        var container = ContainerFromIndex(SelectedIndex)?.As<PivotItem>();

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

        var container = ContainerFromIndex(SelectedIndex)?.As<PivotItem>();
        if (container == args.Item) _ = UpdateCurrentScrollViewer();
    }

    TaskCompletionSource<double?>  tcs = new ();

    private async Task<double?> TryScrollVerticalOffsetAsync(ScrollViewer scrollViewer, CancellationToken cancellationToken = default)
    {
        if (scrollViewer == null) return null;

        double? offsetY = null;
        if (lastScrollOffsetY < MaxHeaderScrollOffset)
            offsetY = Math.Min(MaxHeaderScrollOffset, lastScrollOffsetY);
        else if (scrollViewer.VerticalOffset < MaxHeaderScrollOffset)
            offsetY = MaxHeaderScrollOffset;

        if (offsetY.HasValue)
        {
            if (scrollViewer.ChangeView(null, offsetY.Value, null, true))
            {
                var tcs = new TaskCompletionSource<double?>();

                // 注册当 CancellationToken 被取消时，强制完成 Task
                using var reg = cancellationToken.Register(() => tcs.TrySetResult(scrollViewer.VerticalOffset));

                void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
                {
                    scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                    tcs.TrySetResult(scrollViewer.VerticalOffset);
                }

                scrollViewer.ViewChanged += ScrollViewer_ViewChanged;

                // 为了防止死锁，可以加一个短超时 (例如 500ms)，如果一直不触发，则自动释放
                var delayTask = Task.Delay(1000, cancellationToken);
                var completedTask = await Task.WhenAny(tcs.Task, delayTask);

                if (completedTask == delayTask)
                {
                    // 超时处理：手动卸载事件并返回当前进度
                    scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                    return scrollViewer.VerticalOffset;
                }

                return await tcs.Task;
            }
            scrollViewer.UpdateLayout();
        }
        return null;
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
        Unloaded -= PivotEx_Unloaded;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;

        try
        {
            internalPropSet?.Dispose();
            progressPropSet?.StopAnimation("OffsetY");
            progressPropSet?.StopAnimation("Progress");
            progressPropSet?.Dispose();            
        }
        catch (Exception)
        {
            // ignore
        }

        offsetYBind?.Dispose();
        scrollProgressBind?.Dispose();
        offsetYBind = null;
        scrollProgressBind = null;

        if (currentScrollViewer != null)
        {
            currentScrollViewer.ViewChanging -= CurrentScrollViewer_ViewChanging;
            currentScrollViewer = null;
        }

        SelectionChanged -= PivotEx_SelectionChanged;
        PivotItemLoaded -= PivotEx_PivotItemLoaded;

        currentScrollPropSet = null;
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