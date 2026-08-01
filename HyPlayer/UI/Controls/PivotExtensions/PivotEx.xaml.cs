using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace HyPlayer.UI.Controls;

public sealed partial class PivotEx : Pivot
{
    public static readonly DependencyProperty MaxHeaderScrollOffsetProperty = DependencyProperty.Register(
        nameof(MaxHeaderScrollOffset), typeof(double), typeof(PivotEx),
        new PropertyMetadata(0d, OnMaxHeaderScrollOffsetChanged));

    public static readonly DependencyProperty HeaderScrollOffsetProperty = DependencyProperty.Register(
        nameof(HeaderScrollOffset), typeof(double), typeof(PivotEx),
        new PropertyMetadata(0d, OnHeaderScrollOffsetChanged));

    private readonly CompositionPropertySet _internalPropertySet;
    private readonly CompositionPropertySet _progressPropertySet;
    private CancellationTokenSource? _scrollUpdateCancellation;
    private ScrollViewer? _currentScrollViewer;
    private ExpressionAnimation? _offsetAnimation;
    private ExpressionAnimation? _progressAnimation;
    private double _lastVerticalOffset;
    private bool _isInternalPropertyUpdate;
    private bool _areControlEventsAttached;

    public PivotEx()
    {
        DefaultStyleKey = typeof(PivotEx);

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _progressPropertySet = compositor.CreatePropertySet();
        _progressPropertySet.InsertScalar("Progress", 0);
        _progressPropertySet.InsertScalar("OffsetY", 0);

        _internalPropertySet = compositor.CreatePropertySet();
        _internalPropertySet.InsertScalar("MaxHeaderScrollOffset", 0);
        _internalPropertySet.InsertScalar("Progress", 0);
        _internalPropertySet.InsertScalar("OffsetY", 0);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AttachControlEvents();
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

    public event EventHandler? HeaderScrollOffsetChanged;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StartScrollViewerUpdate();
    }

    internal CompositionPropertySet GetProgressPropertySet() => _progressPropertySet;

    private static void OnMaxHeaderScrollOffsetChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PivotEx pivot)
            return;

        pivot._internalPropertySet.InsertScalar("MaxHeaderScrollOffset", Convert.ToSingle(args.NewValue));
        pivot.UpdateHeaderScrollOffset();
        pivot.UpdateInternalProgress();
    }

    private static void OnHeaderScrollOffsetChanged(DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PivotEx pivot || !pivot._isInternalPropertyUpdate)
            return;

        pivot.UpdateInternalProgress();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachControlEvents();
        StartScrollViewerUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachControlEvents();
        CancelScrollViewerUpdate();
        DetachCurrentScrollViewer();
        StopProgressAnimations();
        _lastVerticalOffset = 0;
    }

    private void AttachControlEvents()
    {
        if (_areControlEventsAttached)
            return;

        SelectionChanged += OnSelectionChanged;
        PivotItemLoaded += OnPivotItemLoaded;
        _areControlEventsAttached = true;
    }

    private void DetachControlEvents()
    {
        if (!_areControlEventsAttached)
            return;

        SelectionChanged -= OnSelectionChanged;
        PivotItemLoaded -= OnPivotItemLoaded;
        _areControlEventsAttached = false;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => StartScrollViewerUpdate();

    private void OnPivotItemLoaded(Pivot sender, PivotItemEventArgs args)
    {
        var scrollViewer = args.Item.FindDescendant<ScrollViewer>();
        if (scrollViewer is not null)
            scrollViewer.IsHitTestVisible = ReferenceEquals(args.Item, ContainerFromIndex(SelectedIndex));

        if (ReferenceEquals(args.Item, ContainerFromIndex(SelectedIndex)))
            StartScrollViewerUpdate();
    }

    private void StartScrollViewerUpdate() => _ = UpdateCurrentScrollViewerAsync();

    private async Task UpdateCurrentScrollViewerAsync()
    {
        var selectedItem = ContainerFromIndex(SelectedIndex) as PivotItem;
        var nextScrollViewer = selectedItem?.FindDescendant<ScrollViewer>();
        if (ReferenceEquals(nextScrollViewer, _currentScrollViewer))
        {
            if (nextScrollViewer is not null)
                nextScrollViewer.IsHitTestVisible = true;
            return;
        }

        CancelScrollViewerUpdate();
        DetachCurrentScrollViewer();

        _currentScrollViewer = nextScrollViewer;
        if (_currentScrollViewer is not null)
        {
            _currentScrollViewer.IsHitTestVisible = true;
            _currentScrollViewer.ViewChanging += OnCurrentScrollViewerViewChanging;
        }

        StartFallbackAnimations();
        if (_currentScrollViewer is null)
            return;

        var cancellation = new CancellationTokenSource();
        _scrollUpdateCancellation = cancellation;
        var token = cancellation.Token;
        var scrollViewer = _currentScrollViewer;

        try
        {
            await SynchronizeVerticalOffsetAsync(scrollViewer, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(scrollViewer, _currentScrollViewer))
                return;

            UpdateHeaderScrollOffset();
            var scrollPropertySet = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(scrollViewer);
            await Task.Delay(200, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(scrollViewer, _currentScrollViewer))
                return;

            StartScrollAnimations(scrollPropertySet);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_scrollUpdateCancellation, cancellation))
                _scrollUpdateCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task<double?> SynchronizeVerticalOffsetAsync(ScrollViewer scrollViewer,
        CancellationToken cancellationToken)
    {
        double? targetOffset = null;
        if (_lastVerticalOffset < MaxHeaderScrollOffset)
            targetOffset = Math.Min(MaxHeaderScrollOffset, _lastVerticalOffset);
        else if (scrollViewer.VerticalOffset < MaxHeaderScrollOffset)
            targetOffset = MaxHeaderScrollOffset;

        if (!targetOffset.HasValue)
            return null;

        var completion = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) =>
            completion.TrySetResult(scrollViewer.VerticalOffset);

        scrollViewer.ViewChanged += OnViewChanged;
        try
        {
            if (!scrollViewer.ChangeView(null, targetOffset.Value, null, true))
            {
                scrollViewer.UpdateLayout();
                return scrollViewer.VerticalOffset;
            }

            using var cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled());
            var completedTask = await Task.WhenAny(completion.Task, Task.Delay(1000, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return completedTask == completion.Task
                ? await completion.Task
                : scrollViewer.VerticalOffset;
        }
        finally
        {
            scrollViewer.ViewChanged -= OnViewChanged;
        }
    }

    private void OnCurrentScrollViewerViewChanging(object sender, ScrollViewerViewChangingEventArgs e) =>
        UpdateHeaderScrollOffset(e.NextView.VerticalOffset);

    private void UpdateHeaderScrollOffset(double? verticalOffset = null)
    {
        var oldValue = HeaderScrollOffset;
        var offset = verticalOffset ?? _currentScrollViewer?.VerticalOffset ?? 0;
        _lastVerticalOffset = Math.Max(0, offset);

        _isInternalPropertyUpdate = true;
        try
        {
            HeaderScrollOffset = Math.Clamp(_lastVerticalOffset, 0, Math.Max(0, MaxHeaderScrollOffset));
        }
        finally
        {
            _isInternalPropertyUpdate = false;
        }

        if (!oldValue.Equals(HeaderScrollOffset))
            HeaderScrollOffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateInternalProgress()
    {
        var maximum = Math.Max(0, MaxHeaderScrollOffset);
        var offset = Math.Clamp(_lastVerticalOffset, 0, maximum);
        _internalPropertySet.InsertScalar("Progress", maximum == 0 ? 0 : (float)(offset / maximum));
        _internalPropertySet.InsertScalar("OffsetY", (float)offset);
    }

    private void StartFallbackAnimations()
    {
        StopProgressAnimations();
        _offsetAnimation = _internalPropertySet.Compositor.CreateExpressionAnimation("prop.OffsetY");
        _offsetAnimation.SetReferenceParameter("prop", _internalPropertySet);
        _progressAnimation = _internalPropertySet.Compositor.CreateExpressionAnimation("prop.Progress");
        _progressAnimation.SetReferenceParameter("prop", _internalPropertySet);
        _progressPropertySet.StartAnimation("OffsetY", _offsetAnimation);
        _progressPropertySet.StartAnimation("Progress", _progressAnimation);
    }

    private void StartScrollAnimations(CompositionPropertySet scrollPropertySet)
    {
        StopProgressAnimations();
        _offsetAnimation = scrollPropertySet.Compositor.CreateExpressionAnimation(
            "clamp(-scroll.Translation.Y, 0, prop.MaxHeaderScrollOffset)");
        _offsetAnimation.SetReferenceParameter("scroll", scrollPropertySet);
        _offsetAnimation.SetReferenceParameter("prop", _internalPropertySet);

        _progressAnimation = scrollPropertySet.Compositor.CreateExpressionAnimation(
            "prop.MaxHeaderScrollOffset == 0 ? 0 : progress.OffsetY / prop.MaxHeaderScrollOffset");
        _progressAnimation.SetReferenceParameter("prop", _internalPropertySet);
        _progressAnimation.SetReferenceParameter("progress", _progressPropertySet);

        _progressPropertySet.StartAnimation("OffsetY", _offsetAnimation);
        _progressPropertySet.StartAnimation("Progress", _progressAnimation);
    }

    private void StopProgressAnimations()
    {
        _progressPropertySet.StopAnimation("OffsetY");
        _progressPropertySet.StopAnimation("Progress");
        _offsetAnimation?.Dispose();
        _progressAnimation?.Dispose();
        _offsetAnimation = null;
        _progressAnimation = null;
    }

    private void CancelScrollViewerUpdate()
    {
        _scrollUpdateCancellation?.Cancel();
        _scrollUpdateCancellation = null;
    }

    private void DetachCurrentScrollViewer()
    {
        if (_currentScrollViewer is null)
            return;

        _currentScrollViewer.ViewChanging -= OnCurrentScrollViewerViewChanging;
        _currentScrollViewer.IsHitTestVisible = false;
        _currentScrollViewer = null;
    }
}
