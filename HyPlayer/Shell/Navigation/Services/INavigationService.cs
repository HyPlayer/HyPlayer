using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Shell.Navigation.Services;

/// <summary>
/// 纯导航服务，封装 Frame 导航操作。
/// 应用级路由由 IAppNavigator 处理；此服务只封装 Frame 导航操作。
/// </summary>
public interface INavigationService
{
    /// <summary>根导航 Frame</summary>
    Frame? RootFrame { get; set; }

    /// <summary>是否可返回（代理 Frame.CanGoBack）</summary>
    bool CanGoBack { get; }

    /// <summary>导航到指定页面</summary>
    void Navigate(Type pageType, object? parameter = null,
                  NavigationTransitionInfo? transition = null);

    /// <summary>返回上一页</summary>
    void NavigateBack();

    /// <summary>刷新当前页面</summary>
    void NavigateRefresh();

    /// <summary>清空 Frame 内容（不导航到具体页面）</summary>
    void ClearContent();
}
