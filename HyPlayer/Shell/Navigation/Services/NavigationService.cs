using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Shell.Navigation.Services;

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
