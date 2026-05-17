using HyPlayer.Classes;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Converters;

/// <summary>
/// NavigationView MenuItem 模板选择器。
/// NavigationNode.IsHeader → HeaderTemplate, NavigationNode.IsSeparator → SeparatorTemplate, 其余 → ItemTemplate。
/// </summary>
public partial class NavigationNodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? SeparatorTemplate { get; set; }
    public DataTemplate? ItemTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is NavigationNode node)
        {
            if (node.IsHeader) return HeaderTemplate;
            if (node.IsSeparator) return SeparatorTemplate;
            return ItemTemplate;
        }
        return ItemTemplate;
    }
}
