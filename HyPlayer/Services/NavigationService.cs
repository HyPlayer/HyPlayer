using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using Microsoft.Toolkit.Uwp.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Services;

/// <summary>
/// 导航服务实现，封装 Frame 导航操作
/// </summary>
public class NavigationService : INavigationService
{
    /// <inheritdoc />
    public Frame? RootFrame { get; set; }

    /// <inheritdoc />
    public Stack<NavigationHistoryItem> NavigationHistory { get; } = new();

    /// <inheritdoc />
    public bool NavigatingBack { get; set; }

    /// <inheritdoc />
    public void Navigate(Type pageType, object? parameter = null)
    {
        var setting = Ioc.Default.GetRequiredService<Setting>();
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        if (setting.forceMemoryGarbage)
        {
            var pageBase = uiState.PageBase as BasePage;
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
        var setting = Ioc.Default.GetRequiredService<Setting>();
        if (setting.forceMemoryGarbage)
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
        var peek = NavigationHistory.Peek();
        RootFrame?.Navigate(peek.PageType, peek.Paratmers);
        GC.Collect();
    }

    /// <inheritdoc />
    public void CollectGarbage()
    {
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        Navigate(typeof(BlankPage));
        RootFrame!.Content = null;
        uiState.PageExpandedPlayer = null;
        var pageMain = uiState.PageMain as MainPage;
        pageMain?.ExpandedPlayer.Navigate(typeof(BlankPage));
        _ = ImageCache.Instance.ClearAsync();
        uiState.KawazuConv?.Dispose();
        uiState.KawazuConv = null;
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
                var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
                await playlist.AppendNcSourceAsync(resourceId);
                playlist.MoveToAsync(playlist.Items.FirstOrDefault(t => "ns" + t.Id == resourceId));
                break;
            case "ml":
                Navigate(typeof(MVPage), resourceId[2..]);
                break;
        }
    }
}
