using System;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Controls;

public class PivotExHeaderView : ListView
{
    public static readonly DependencyProperty PivotProperty =
        DependencyProperty.Register("Pivot", typeof(PivotEx), typeof(PivotExHeaderView), new PropertyMetadata(null,
            (s, a) =>
            {
                if (s is PivotExHeaderView sender)
                {
                    if (a.OldValue is PivotEx oldValue)
                    {
                        sender.ItemsSource = null;
                        sender.ItemTemplateSelector = null;
                        oldValue.SelectionChanged -= sender.Pivot_SelectionChanged;
                        oldValue.Items.VectorChanged -= sender.Pivot_ItemsChanged;
                        oldValue.UnregisterPropertyChangedCallback(
                            Windows.UI.Xaml.Controls.Pivot.HeaderTemplateProperty,
                            sender.pivotHeaderTemplateEventToken);
                    }

                    if (a.NewValue is PivotEx newValue)
                    {
                        newValue.SelectionChanged += sender.Pivot_SelectionChanged;
                        newValue.Items.VectorChanged += sender.Pivot_ItemsChanged;
                        sender.pivotHeaderTemplateEventToken = newValue.RegisterPropertyChangedCallback(
                            Windows.UI.Xaml.Controls.Pivot.HeaderTemplateProperty, sender.OnHeaderTemplateChanged);
                    }

                    sender.ItemTemplateSelector = new PivotHeaderTemplateSelector(sender);
                    sender.UpdateHeaderItemsSource();
                }
            }));

    private DataTemplate DefaultHeaderTemplate;
    private DataTemplate EmptyHeaderTemplate;

    private Border LayoutRoot;
    private long pivotHeaderTemplateEventToken;

    public PivotExHeaderView()
    {
        DefaultStyleKey = typeof(PivotExHeaderView);

        SelectionChanged += PivotExHeaderView_SelectionChanged;
    }

    public PivotEx Pivot
    {
        get => (PivotEx)GetValue(PivotProperty);
        set => SetValue(PivotProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        LayoutRoot = GetTemplateChild(nameof(LayoutRoot)) as Border;
        DefaultHeaderTemplate = LayoutRoot?.Resources["DefaultHeaderTemplate"] as DataTemplate;
        EmptyHeaderTemplate = LayoutRoot?.Resources["EmptyHeaderTemplate"] as DataTemplate;

        ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
    }

    private void Pivot_ItemsChanged(IObservableVector<object> sender, IVectorChangedEventArgs @event)
    {
        UpdateHeaderItemsSource();
    }

    private void PivotExHeaderView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var container = ContainerFromIndex(SelectedIndex) as FrameworkElement;
        if (container != null) container.StartBringIntoView();

        if (Pivot != null && SelectedIndex != Pivot.SelectedIndex)
        {
            if (SelectedIndex == -1)
                SelectedIndex = Pivot.SelectedIndex;
            else
                Pivot.SelectedIndex = SelectedIndex;
        }
    }

    private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pivot != null && SelectedIndex != Pivot.SelectedIndex) SelectedIndex = Pivot.SelectedIndex;
    }


    private void OnHeaderTemplateChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (Pivot != null) ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
    }

    private void UpdateHeaderItemsSource()
    {
        ItemsSource = Pivot.Items?
            .Select(c => c switch
            {
                PivotItem pivotItem => new PivotItemHeaderWrapper(pivotItem),
                UIElement => throw new ArgumentException("PivotItem.Header"),
                _ => c
            })
            .ToList();
    }


    private class PivotItemHeaderWrapper : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs headerPropertyChangedEventArgs = new(nameof(Header));

        private PivotItem pivotItem;
        private readonly long pivotItemHeaderEventToken;

        public PivotItemHeaderWrapper(PivotItem pivotItem)
        {
            this.pivotItem = pivotItem ?? throw new ArgumentNullException(nameof(pivotItem));

            pivotItemHeaderEventToken =
                pivotItem.RegisterPropertyChangedCallback(PivotItem.HeaderProperty, PivotItemHeaderChanged);
        }

        public object Header => pivotItem.Header;

        public event PropertyChangedEventHandler PropertyChanged;

        private void PivotItemHeaderChanged(DependencyObject sender, DependencyProperty dp)
        {
            PropertyChanged?.Invoke(this, headerPropertyChangedEventArgs);
        }

        ~PivotItemHeaderWrapper()
        {
            pivotItem.UnregisterPropertyChangedCallback(PivotItem.HeaderProperty, pivotItemHeaderEventToken);
            pivotItem = null!;
        }
    }

    private class PivotHeaderTemplateSelector : DataTemplateSelector
    {
        private readonly PivotExHeaderView view;

        public PivotHeaderTemplateSelector(PivotExHeaderView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
        }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            if (item is PivotItemHeaderWrapper wrapper && wrapper.Header is not UIElement)
                return view.DefaultHeaderTemplate;
            if (item is not UIElement)
                return view?.Pivot.HeaderTemplate;
            return view.EmptyHeaderTemplate;
        }
    }
}