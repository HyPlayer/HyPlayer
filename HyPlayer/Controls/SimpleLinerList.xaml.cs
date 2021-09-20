#region

using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;

#endregion

namespace HyPlayer.Controls
{
    public partial class SimpleLinerList : UserControl
    {
        public static readonly DependencyProperty ListItemsProperty = DependencyProperty.Register(
            "ListItems", typeof(ObservableCollection<SimpleListItem>),
            typeof(SimpleListItem),
            new PropertyMetadata(new ObservableCollection<SimpleListItem>())
        );

        public SimpleLinerList()
        {
            InitializeComponent();
        }

        public ObservableCollection<SimpleListItem> ListItems
        {
            get => (ObservableCollection<SimpleListItem>)GetValue(ListItemsProperty);
            set => SetValue(ListItemsProperty, value);
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItemList.SelectedIndex >= 0)
                Common.NavigatePageResource(ListItems[ItemList.SelectedIndex].ResourceId);
        }
    }
}