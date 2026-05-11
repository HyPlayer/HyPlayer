using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Services;

/// <summary>
/// 导航服务实现，封装 Frame 导航操作
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IBackgroundTaskRunner _taskRunner;
    private readonly Setting _setting;
    private readonly IUIStateService _uiState;
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(
        IBackgroundTaskRunner taskRunner,
        Setting setting,
        IUIStateService uiState,
        IServiceProvider serviceProvider)
    {
        _taskRunner = taskRunner;
        _setting = setting;
        _uiState = uiState;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Frame? RootFrame { get; set; }

    /// <inheritdoc />
    public Stack<NavigationHistoryItem> NavigationHistory { get; } = new();

    /// <inheritdoc />
    public bool NavigatingBack { get; set; }

    /// <inheritdoc />
    public void Navigate(Type pageType, object? parameter = null)
    {
        if (_setting.forceMemoryGarbage)
        {
            var pageBase = _uiState.PageBase as BasePage;
            if (NavigationHistory.Count >= 1 && pageBase?.NavMain.SelectedItem == NavigationHistory.Peek().Item)
                pageBase.NavMain.SelectedItem = pageBase.NavItemBlank;
            NavigationHistory.Push(new NavigationHistoryItem
            {
                PageType = pageType,
                Paratmers = parameter,
                Item = pageBase?.NavMain.SelectedItem
            });
            RootFrame?.Navigate(pageType, parameter,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
            GC.Collect();
        }
        else
        {
            RootFrame?.Navigate(pageType, parameter,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
    }

    /// <inheritdoc />
    public void NavigateBack()
    {
        if (_setting.forceMemoryGarbage)
        {
            if (NavigationHistory.Count > 1)
                NavigationHistory.Pop();
            try
            {
                var bak = NavigationHistory.Peek();
                while (bak.PageType == typeof(BlankPage))
                {
                    NavigationHistory.Pop();
                    bak = NavigationHistory.Peek();
                }

                RootFrame?.Navigate(bak.PageType, bak.Paratmers,
                    new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
                NavigatingBack = true;
                NavigatingBack = false;
                GC.Collect();
            }
            catch
            {
            }
        }
        else
        {
            if (RootFrame != null && RootFrame.CanGoBack)
                RootFrame?.GoBack();
        }
    }

    /// <inheritdoc />
    public void NavigateRefresh()
    {
        if (NavigationHistory.Count == 0)
            return;

        var peek = NavigationHistory.Peek();
        RootFrame?.Navigate(peek.PageType, peek.Paratmers);
        GC.Collect();
    }

    /// <inheritdoc />
    public void CollectGarbage()
    {
        if (RootFrame is null)
            return;

        Navigate(typeof(BlankPage));
        RootFrame.Content = null;
        var pageMain = _uiState.PageMain as MainPage;
        pageMain?.ExpandedPlayer.Navigate(typeof(BlankPage));
        _taskRunner.Forget(ImageCache.Instance.ClearAsync(), "clear image cache while collecting garbage");
        _uiState.KawazuConv?.Dispose();
        _uiState.KawazuConv = null;
    }

    /// <inheritdoc />
    public async Task NavigateToResourceAsync(string resourceId)
    {
        switch (resourceId[..2])
        {
            case "al":
                Navigate(typeof(AlbumPage), resourceId[2..]);
                break;
            case "pl":
                Navigate(typeof(SongListDetail), resourceId[2..]);
                break;
            case "rd":
                Navigate(typeof(RadioPage), resourceId[2..]);
                break;
            case "ar":
                Navigate(typeof(ArtistPage), resourceId[2..]);
                break;
            case "us":
                Navigate(typeof(Me), resourceId[2..]);
                break;
            case "ns":
                var playlist = _serviceProvider.GetRequiredService<IPlaylistService>();
                await playlist.AppendNcSourceAsync(resourceId);
                var item = playlist.Items.FirstOrDefault(t => "ns" + t.Id == resourceId);
                if (item is not null)
                    await playlist.MoveToAsync(item);
                break;
            case "ml":
                Navigate(typeof(MVPage), resourceId[2..]);
                break;
        }
    }
}
