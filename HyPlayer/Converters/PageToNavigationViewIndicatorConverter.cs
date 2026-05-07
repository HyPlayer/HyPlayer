using System;
using System.Linq;
using HyPlayer.Pages;
using Windows.UI.Xaml.Data;
using Microsoft.UI.Xaml.Controls;
using WinRT;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;

using HyPlayer.Services.Abstractions;
using CommunityToolkit.Mvvm.DependencyInjection;
namespace HyPlayer.Classes
{
    public partial class PageToNavigationViewIndicatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            NavigationViewItem pageNavigationViewItem;
            if (value == null)
            {
                return (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage)?.NavItemBlank;
            }
            Type pageType = value.GetType();
            if (pageType == typeof(HomePage))
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage)?.NavItemPageHome;
            else if (pageType == typeof(SongListDetail))
            {
                var displayedList = (SongListDetail)value;
                if (displayedList.ViewModel.PlayList == null) return (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage)?.NavItemBlank;
                if (displayedList.ViewModel.PlayList.Name == "每日歌曲推荐")
                    pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemDailyRcmd;
                else if (displayedList.ViewModel.PlayList.PlaylistId == Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].PlaylistId)
                    pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemsMyLovedPlaylist;
                else
                {
                    var item = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemsMyList.MenuItems.Where(t => (t?.As<NavigationViewItem>()?.Tag as string) == $"Playlist{displayedList.ViewModel.PlayList.PlaylistId}").FirstOrDefault()
                        ?? (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemsLikeList.MenuItems.Where(t => (t?.As<NavigationViewItem>()?.Tag as string) == $"Playlist{displayedList.ViewModel.PlayList.PlaylistId}").FirstOrDefault();
                    if (item != null)
                    {
                        pageNavigationViewItem = (NavigationViewItem)item;
                    }
                    else
                    {
                        pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank;
                    }
                }
            }
            else if (pageType == typeof(LocalMusicPage))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemPageLocal;
            }
            else if (pageType == typeof(History))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).PageHistory;
            }
            else if (pageType == typeof(PageFavorite))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemPageFavorite;
            }
            else if (pageType == typeof(MusicCloudPage))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemMusicCloud;
            }
            else if (pageType == typeof(HyPlayer.Pages.Settings))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemPageSettings;
            }
            else if (pageType == typeof(Me))
            {
                pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemLogin;
            }
            else pageNavigationViewItem = (Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage).NavItemBlank;
            return pageNavigationViewItem;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
