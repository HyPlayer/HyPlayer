using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Controls
{
    public class PivotExHeaderView : ListView
    {
        private long pivotHeaderTemplateEventToken;

        public PivotExHeaderView()
        {
            this.DefaultStyleKey = typeof(PivotExHeaderView);

            this.SelectionChanged += PivotExHeaderView_SelectionChanged;
        }

        private Border LayoutRoot;
        private DataTemplate DefaultHeaderTemplate;
        private DataTemplate EmptyHeaderTemplate;

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            LayoutRoot = GetTemplateChild(nameof(LayoutRoot)) as Border;
            DefaultHeaderTemplate = LayoutRoot?.Resources["DefaultHeaderTemplate"] as DataTemplate;
            EmptyHeaderTemplate = LayoutRoot?.Resources["EmptyHeaderTemplate"] as DataTemplate;

            ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
        }

        public PivotEx Pivot
        {
            get { return (PivotEx)GetValue(PivotProperty); }
            set { SetValue(PivotProperty, value); }
        }

        public static readonly DependencyProperty PivotProperty =
            DependencyProperty.Register("Pivot", typeof(PivotEx), typeof(PivotExHeaderView), new PropertyMetadata(null, (s, a) =>
            {
                if (s is PivotExHeaderView sender)
                {
                    if (a.OldValue is PivotEx oldValue)
                    {
                        sender.ItemsSource = null;
                        sender.ItemTemplateSelector = null;
                        oldValue.SelectionChanged -= sender.Pivot_SelectionChanged;
                        oldValue.Items.VectorChanged -= sender.Pivot_ItemsChanged;
                        oldValue.UnregisterPropertyChangedCallback(PivotEx.HeaderTemplateProperty, sender.pivotHeaderTemplateEventToken);
                    }
                    if (a.NewValue is PivotEx newValue)
                    {
                        newValue.SelectionChanged += sender.Pivot_SelectionChanged;
                        newValue.Items.VectorChanged += sender.Pivot_ItemsChanged;
                        sender.pivotHeaderTemplateEventToken = newValue.RegisterPropertyChangedCallback(PivotEx.HeaderTemplateProperty, sender.OnHeaderTemplateChanged);
                    }

                    sender.ItemTemplateSelector = new PivotHeaderTemplateSelector(sender);
                    sender.UpdateHeaderItemsSource();
                }
            }));

        private void Pivot_ItemsChanged(Windows.Foundation.Collections.IObservableVector<object> sender, Windows.Foundation.Collections.IVectorChangedEventArgs @event)
        {
            UpdateHeaderItemsSource();
        }

        private void PivotExHeaderView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var container = ContainerFromIndex(SelectedIndex) as FrameworkElement;
            if (container != null)
            {
                container.StartBringIntoView();
            }

            if (Pivot != null && SelectedIndex != Pivot.SelectedIndex)
            {
                if (SelectedIndex == -1)
                {
                    SelectedIndex = Pivot.SelectedIndex;
                }
                else
                {
                    Pivot.SelectedIndex = SelectedIndex;
                }
            }
        }

        private void Pivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Pivot != null && SelectedIndex != Pivot.SelectedIndex)
            {
                SelectedIndex = Pivot.SelectedIndex;
            }
        }


        private void OnHeaderTemplateChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (Pivot != null)
            {
                ItemTemplateSelector = new PivotHeaderTemplateSelector(this);
            }
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
            private readonly static PropertyChangedEventArgs headerPropertyChangedEventArgs = new PropertyChangedEventArgs(nameof(Header));

            private PivotItem pivotItem;
            private long pivotItemHeaderEventToken;

            public PivotItemHeaderWrapper(PivotItem pivotItem)
            {
                this.pivotItem = pivotItem ?? throw new ArgumentNullException(nameof(pivotItem));

                pivotItemHeaderEventToken = pivotItem.RegisterPropertyChangedCallback(PivotItem.HeaderProperty, PivotItemHeaderChanged);
            }

            public object Header => pivotItem.Header;

            private void PivotItemHeaderChanged(DependencyObject sender, DependencyProperty dp)
            {
                PropertyChanged?.Invoke(this, headerPropertyChangedEventArgs);
            }

            public event PropertyChangedEventHandler PropertyChanged;

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
                {
                    return view.DefaultHeaderTemplate;
                }
                else if (item is not UIElement)
                {
                    return view?.Pivot.HeaderTemplate;
                }
                else
                {
                    return view.EmptyHeaderTemplate;
                }
            }
        }
    }
}
