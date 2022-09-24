#region

using System.Collections.ObjectModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;

#endregion

namespace HyPlayer.Controls;

public partial class SimpleLinerList : UserControl
{
    public static readonly DependencyProperty ListItemsProperty = DependencyProperty.Register(
        "ListItems", typeof(ObservableCollection<SimpleListItem>),
        typeof(SimpleListItem),
        new PropertyMetadata(new ObservableCollection<SimpleListItem>())
    );

    public static readonly DependencyProperty ListHeaderProperty = DependencyProperty.Register(
        "ListHeader", typeof(UIElement), typeof(SimpleLinerList), new PropertyMetadata(default(UIElement)));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        "Footer", typeof(UIElement), typeof(SimpleLinerList), new PropertyMetadata(default(UIElement)));

    public SimpleLinerList()
    {
        InitializeComponent();
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

    public ObservableCollection<SimpleListItem> ListItems
    {
        get => (ObservableCollection<SimpleListItem>)GetValue(ListItemsProperty);
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
        HyPlayList.SongAppendDone();
        if (((Button)sender).Tag.ToString().Substring(0, 2) == "pl" ||
            ((Button)sender).Tag.ToString().Substring(0, 2) == "al")
            HyPlayList.PlaySourceId = ((Button)sender).Tag.ToString().Substring(2);
        HyPlayList.NowPlaying = -1;
        HyPlayList.SongMoveNext();
    }
}