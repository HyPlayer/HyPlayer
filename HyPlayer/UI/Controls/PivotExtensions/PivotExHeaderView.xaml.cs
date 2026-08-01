using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;

namespace HyPlayer.UI.Controls;

public sealed partial class PivotExHeaderView : ListView
{
    public static readonly DependencyProperty PivotProperty = DependencyProperty.Register(
        nameof(Pivot), typeof(PivotEx), typeof(PivotExHeaderView), new PropertyMetadata(null, OnPivotChanged));

    private DataTemplate? _defaultHeaderTemplate;
    private DataTemplate? _emptyHeaderTemplate;
    private PivotEx? _currentPivot;
    private long _headerTemplatePropertyToken;
    private bool _isHeaderTemplateCallbackRegistered;
    private bool _isPivotSubscribed;
    private bool _isSelectionChangedSubscribed;

    public PivotExHeaderView()
    {
        DefaultStyleKey = typeof(PivotExHeaderView);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        AttachSelectionChanged();
    }

    public PivotEx? Pivot
    {
        get => (PivotEx?)GetValue(PivotProperty);
        set => SetValue(PivotProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        var layoutRoot = GetTemplateChild("LayoutRoot") as Border;
        _defaultHeaderTemplate = layoutRoot?.Resources["DefaultHeaderTemplate"] as DataTemplate;
        _emptyHeaderTemplate = layoutRoot?.Resources["EmptyHeaderTemplate"] as DataTemplate;
        ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
    }

    private static void OnPivotChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PivotExHeaderView view)
            return;

        view.DetachPivot();
        view._currentPivot = args.NewValue as PivotEx;
        view.AttachPivot();
        view.UpdateHeaderItemsSource();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachSelectionChanged();
        AttachPivot();
        UpdateHeaderItemsSource();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachSelectionChanged();
        DetachPivot();
        DisposeCurrentItemsSource();
        ItemsSource = null;
        ItemTemplateSelector = null;
    }

    private void AttachSelectionChanged()
    {
        if (_isSelectionChangedSubscribed)
            return;

        SelectionChanged += OnHeaderSelectionChanged;
        _isSelectionChangedSubscribed = true;
    }

    private void DetachSelectionChanged()
    {
        if (!_isSelectionChangedSubscribed)
            return;

        SelectionChanged -= OnHeaderSelectionChanged;
        _isSelectionChangedSubscribed = false;
    }

    private void AttachPivot()
    {
        if (_currentPivot is null || _isPivotSubscribed)
            return;

        _currentPivot.SelectionChanged += OnPivotSelectionChanged;
        _currentPivot.Items.VectorChanged += OnPivotItemsChanged;
        _headerTemplatePropertyToken = _currentPivot.RegisterPropertyChangedCallback(
            Windows.UI.Xaml.Controls.Pivot.HeaderTemplateProperty, OnPivotHeaderTemplateChanged);
        _isHeaderTemplateCallbackRegistered = true;
        _isPivotSubscribed = true;
    }

    private void DetachPivot()
    {
        if (_currentPivot is null)
            return;

        if (_isPivotSubscribed)
        {
            _currentPivot.SelectionChanged -= OnPivotSelectionChanged;
            _currentPivot.Items.VectorChanged -= OnPivotItemsChanged;
            _isPivotSubscribed = false;
        }

        if (_isHeaderTemplateCallbackRegistered)
        {
            _currentPivot.UnregisterPropertyChangedCallback(
                Windows.UI.Xaml.Controls.Pivot.HeaderTemplateProperty, _headerTemplatePropertyToken);
            _isHeaderTemplateCallbackRegistered = false;
        }
    }

    private void OnPivotItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs args) =>
        UpdateHeaderItemsSource();

    private void OnHeaderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentPivot is null)
            return;

        if (ContainerFromIndex(SelectedIndex) is FrameworkElement container)
            container.StartBringIntoView();

        if (SelectedIndex == _currentPivot.SelectedIndex)
            return;

        if (SelectedIndex == -1)
            SelectedIndex = _currentPivot.SelectedIndex;
        else
            _currentPivot.SelectedIndex = SelectedIndex;
    }

    private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentPivot is not null && SelectedIndex != _currentPivot.SelectedIndex)
            SelectedIndex = _currentPivot.SelectedIndex;
    }

    private void OnPivotHeaderTemplateChanged(DependencyObject sender, DependencyProperty dependencyProperty)
    {
        ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
    }

    private void UpdateHeaderItemsSource()
    {
        DisposeCurrentItemsSource();
        ItemsSource = _currentPivot?.Items
            .Select(item => item is PivotItem pivotItem ? new PivotItemHeaderWrapper(pivotItem) : item)
            .ToList();
        ItemTemplateSelector = new PivotHeaderTemplateSelector(this);

        if (_currentPivot is not null)
            SelectedIndex = _currentPivot.SelectedIndex;
    }

    private void DisposeCurrentItemsSource()
    {
        if (ItemsSource is not IEnumerable<object> items)
            return;

        foreach (var item in items.OfType<IDisposable>())
            item.Dispose();
    }

    [GeneratedBindableCustomProperty]
    private sealed partial class PivotItemHeaderWrapper : INotifyPropertyChanged, IDisposable
    {
        private static readonly PropertyChangedEventArgs HeaderPropertyChangedEventArgs = new(nameof(Header));
        private readonly long _headerPropertyToken;
        private PivotItem? _pivotItem;

        public PivotItemHeaderWrapper(PivotItem pivotItem)
        {
            _pivotItem = pivotItem ?? throw new ArgumentNullException(nameof(pivotItem));
            _headerPropertyToken = pivotItem.RegisterPropertyChangedCallback(
                PivotItem.HeaderProperty, OnHeaderPropertyChanged);
        }

        public object? Header => _pivotItem?.Header;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Dispose()
        {
            if (_pivotItem is null)
                return;

            _pivotItem.UnregisterPropertyChangedCallback(PivotItem.HeaderProperty, _headerPropertyToken);
            _pivotItem = null;
            PropertyChanged = null;
        }

        private void OnHeaderPropertyChanged(DependencyObject sender, DependencyProperty dependencyProperty) =>
            PropertyChanged?.Invoke(this, HeaderPropertyChangedEventArgs);
    }

    private sealed partial class PivotHeaderTemplateSelector : DataTemplateSelector
    {
        private readonly PivotExHeaderView _view;

        public PivotHeaderTemplateSelector(PivotExHeaderView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is PivotItemHeaderWrapper { Header: not UIElement })
                return _view._defaultHeaderTemplate;
            if (item is not UIElement)
                return _view._currentPivot?.HeaderTemplate;
            return _view._emptyHeaderTemplate;
        }
    }
}
