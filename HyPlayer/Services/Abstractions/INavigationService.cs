using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

using HyPlayer.Services.Abstractions;
namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 导航服务，封装 Frame 导航操作
/// </summary>
public interface INavigationService
{
    /// <summary>根导航 Frame</summary>
    Frame? RootFrame { get; set; }

    /// <summary>导航到指定页面</summary>
    void Navigate(Type pageType, object? parameter = null);

    /// <summary>返回上一页</summary>
    void NavigateBack();

    /// <summary>根据资源 ID 导航（如 al/pl/ar/us 等前缀）</summary>
    Task NavigateToResourceAsync(string resourceId);

    /// <summary>导航历史栈</summary>
    Stack<NavigationHistoryItem> NavigationHistory { get; }

    /// <summary>是否正在返回导航</summary>
    bool NavigatingBack { get; set; }

    /// <summary>刷新当前页面</summary>
    void NavigateRefresh();

    /// <summary>回收内存</summary>
    void CollectGarbage();
}

public class NavigationHistoryItem
{
    public object Item;
    public Type PageType;
    public object Paratmers;
}
