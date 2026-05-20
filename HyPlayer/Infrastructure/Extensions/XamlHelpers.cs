using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using WinRT;

namespace HyPlayer.Infrastructure.Extensions;

/// <summary>
/// XAML x:Bind 专用静态辅助类。
/// 仅包含 XAML 绑定所需的静态属性和事件处理器，不包含业务逻辑。
/// </summary>
internal static class XamlHelpers
{
    /// <summary>
    /// 全局 Setting 实例，供 XAML x:Bind 使用。
    /// </summary>
    public static Setting Setting => Ioc.Default.GetRequiredService<Setting>();

    /// <summary>
    /// 通用右键菜单处理器，供 XAML RightTapped 事件绑定。
    /// </summary>
    public static void UIElement_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var element = sender?.As<UIElement>();
        try
        {
            element?.ContextFlyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        }
        catch
        {
            var flyout = FlyoutBase.GetAttachedFlyout(element?.As<FrameworkElement>()!);
            flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        }
    }
}
