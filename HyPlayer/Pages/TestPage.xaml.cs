using HyPlayer.HyPlayControl;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class TestPage : Page
{
    private int _teachingTipIndex;



    public string ResourceId
    {
        get { return (string)GetValue(ResourceIdProperty); }
        set { SetValue(ResourceIdProperty, value); }
    }
    public static readonly DependencyProperty ResourceIdProperty = DependencyProperty.Register("ResourceId", typeof(string), typeof(TestPage), new PropertyMetadata(""));
    


    public TestPage()
    {
        InitializeComponent();
    }

    private void TestTeachingTip_OnClick(object sender, RoutedEventArgs e)
    {
        Common.AddToTeachingTipLists("TestTeachingTip", _teachingTipIndex++.ToString());
    }

    private void NavigateResourceId(object sender, RoutedEventArgs e)
    {
        Common.NavigatePageResource(ResourceId);
    }

    private void PlayResourceId(object sender, RoutedEventArgs e)
    {
        HyPlayList.AppendNcSource(ResourceId);
    }
}