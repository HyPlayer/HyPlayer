using HyPlayer.Services.Abstractions;
using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Services.Navigation;

/// <summary>
/// 纯导航服务实现，使用 Frame 原生导航栈。
/// 资源路由、内存管理、导航历史等职责已移除至独立接口。
/// </summary>
public class NavigationService : INavigationService
{
    private Type? _lastPageType;
    private object? _lastParameter;

    /// <inheritdoc />
    public Frame? RootFrame { get; set; }

    /// <inheritdoc />
    public bool CanGoBack => RootFrame?.CanGoBack ?? false;

    /// <inheritdoc />
    public void Navigate(Type pageType, object? parameter = null,
                         NavigationTransitionInfo? transition = null)
    {
        if (RootFrame?.CurrentSourcePageType == pageType &&
            _lastPageType == pageType &&
            Equals(_lastParameter, parameter))
            return;

        if (RootFrame?.Navigate(pageType, parameter, transition
            ?? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight }) == true)
        {
            _lastPageType = pageType;
            _lastParameter = parameter;
        }
    }

    /// <inheritdoc />
    public void NavigateBack()
    {
        if (RootFrame?.CanGoBack == true)
        {
            RootFrame.GoBack();
            _lastPageType = RootFrame.CurrentSourcePageType;
            _lastParameter = null;
        }
    }

    /// <inheritdoc />
    public void NavigateRefresh()
    {
        if (RootFrame?.Content is null) return;
        RootFrame.Navigate(RootFrame.CurrentSourcePageType);
        _lastPageType = RootFrame.CurrentSourcePageType;
        _lastParameter = null;
    }

    /// <inheritdoc />
    public void ClearContent()
    {
        if (RootFrame is not null)
        {
            RootFrame.Content = null;
            _lastPageType = null;
            _lastParameter = null;
        }
    }
}
