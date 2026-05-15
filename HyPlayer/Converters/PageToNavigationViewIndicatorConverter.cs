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
            var basePage = Ioc.Default.GetRequiredService<IUIStateService>().PageBase as BasePage;
            NavigationViewItem pageNavigationViewItem;
            if (value == null)
            {
                return basePage?.NavItemBlank;
            }
            Type pageType = value.GetType();
            if (pageType == typeof(HomePage))
                pageNavigationViewItem = basePage?.NavItemPageHome;
            else if (pageType == typeof(SongListDetail))
            {
                var displayedList = (SongListDetail)value;
                if (displayedList.ViewModel.PlayList == null) return basePage?.NavItemBlank;
                if (displayedList.ViewModel.PlayList.Name == "每日歌曲推荐")
                    pageNavigationViewItem = basePage.NavItemDailyRcmd;
                else if (displayedList.ViewModel.PlayList.PlaylistId == Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].PlaylistId)
                    pageNavigationViewItem = basePage.NavItemsMyLovedPlaylist;
                else
                {
                    var item = basePage.NavItemsMyList.MenuItems.Where(t => (t?.As<NavigationViewItem>()?.Tag as string) == $"Playlist{displayedList.ViewModel.PlayList.PlaylistId}").FirstOrDefault()
                        ?? basePage.NavItemsLikeList.MenuItems.Where(t => (t?.As<NavigationViewItem>()?.Tag as string) == $"Playlist{displayedList.ViewModel.PlayList.PlaylistId}").FirstOrDefault();
                    if (item != null)
                    {
                        pageNavigationViewItem = (NavigationViewItem)item;
                    }
                    else
                    {
                        pageNavigationViewItem = basePage.NavItemBlank;
                    }
                }
            }
            else if (pageType == typeof(LocalMusicPage))
            {
                pageNavigationViewItem = basePage.NavItemPageLocal;
            }
            else if (pageType == typeof(History))
            {
                pageNavigationViewItem = basePage.PageHistory;
            }
            else if (pageType == typeof(PageFavorite))
            {
                pageNavigationViewItem = basePage.NavItemPageFavorite;
            }
            else if (pageType == typeof(MusicCloudPage))
            {
                pageNavigationViewItem = basePage.NavItemMusicCloud;
            }
            else if (pageType == typeof(HyPlayer.Pages.Settings))
            {
                pageNavigationViewItem = basePage.NavItemPageSettings;
            }
            else if (pageType == typeof(Me))
            {
                pageNavigationViewItem = basePage.NavItemLogin;
            }
            else pageNavigationViewItem = basePage.NavItemBlank;
            return pageNavigationViewItem;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
