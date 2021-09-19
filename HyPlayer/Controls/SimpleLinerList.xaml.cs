using HyPlayer.Classes;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Controls
{
    public partial class SimpleLinerList : UserControl
    {
        public static readonly DependencyProperty ListItemsProperty = DependencyProperty.Register(
            "ListItems", typeof(ObservableCollection<SimpleListItem>),
            typeof(SimpleListItem),
            new PropertyMetadata(new ObservableCollection<SimpleListItem>())
        );

        public ObservableCollection<SimpleListItem> ListItems
        {
            get { return (ObservableCollection<SimpleListItem>)GetValue(ListItemsProperty); }
            set { SetValue(ListItemsProperty, value); }
        }

        public SimpleLinerList()
        {
            InitializeComponent();
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItemList.SelectedIndex >= 0)
                Common.NavigatePageResource(ListItems[ItemList.SelectedIndex].ResourceId);
        }
    }
}