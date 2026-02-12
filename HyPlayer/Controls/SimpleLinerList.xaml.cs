#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using ObservableCollections;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

#endregion

namespace HyPlayer.Controls;

public partial class SimpleLinerList : UserControl
{
    public static readonly DependencyProperty ListItemsProperty = DependencyProperty.Register(
        "ListItems", typeof(ObservableList<SimpleListItem>),
        typeof(SimpleListItem),
        new PropertyMetadata(new ObservableList<SimpleListItem>())
    );

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        "ListHeader", typeof(UIElement), typeof(SimpleLinerList), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        "Footer", typeof(UIElement), typeof(SimpleLinerList), new PropertyMetadata(default(UIElement)));

    public SimpleLinerList()
    {
        InitializeComponent();
        ItemList.ItemsSource = ListItems.ToNotifyCollectionChanged();
    }

    public UIElement ListHeader
    {
        get => (UIElement)GetValue(ListHeaderProperty);
        set => SetValue(ListHeaderProperty, value);
    }

    public UIElement Footer
    {
        get => (UIElement)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public ObservableList<SimpleListItem> ListItems
    {
        get => (ObservableList<SimpleListItem>)GetValue(ListItemsProperty);
        set => SetValue(ListItemsProperty, value);
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedIndex >= 0)
            _ = Common.NavigatePageResource(ListItems[ItemList.SelectedIndex].ResourceId);
    }

    private async void BtnPlayClick(object sender, RoutedEventArgs e)
    {
        HyPlayList.RemoveAllSong();
        await HyPlayList.AppendNcSource(((Button)sender).Tag.ToString());
        if (((Button)sender).Tag.ToString().Substring(0, 2) == "pl" ||
            ((Button)sender).Tag.ToString().Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ((Button)sender).Tag.ToString().Substring(2);
        HyPlayList.NowPlaying = -1;
        HyPlayList.SongMoveNext();
    }
}