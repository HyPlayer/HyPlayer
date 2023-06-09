using HyPlayer.Pages;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Converters
{
    public class PageToNavigationViewIndicatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            NavigationViewItem pageNavigationViewItem;
            if (value == null)
            {
                return Common.PageBase.NavItemBlank;
            }
            Type pageType = value.GetType();
            if (pageType == typeof(Home))
                pageNavigationViewItem = Common.PageBase.NavItemPageHome;
            else if (pageType == typeof(SongListDetail))
            {
                var DisplayedList = (SongListDetail)value;
                while (DisplayedList.playList == null)
                {
                    Task.Delay(20);
                }
                if (DisplayedList.playList.name == "每日歌曲推荐")
                    pageNavigationViewItem = Common.PageBase.NavItemDailyRcmd;
                else if (DisplayedList.playList.plid == Common.MySongLists[0].plid)
                    pageNavigationViewItem = Common.PageBase.NavItemsMyLovedPlaylist;
                else pageNavigationViewItem = Common.PageBase.NavItemBlank;
            }
            else if (pageType == typeof(LocalMusicPage))
            {
                pageNavigationViewItem = Common.PageBase.NavItemPageLocal;
            }
            else if (pageType == typeof(History))
            {
                pageNavigationViewItem = Common.PageBase.PageHistory;
            }
            else if (pageType == typeof(PageFavorite))
            {
                pageNavigationViewItem = Common.PageBase.NavItemPageFavorite;
            }
            else if (pageType == typeof(MusicCloudPage))
            {
                pageNavigationViewItem = Common.PageBase.NavItemMusicCloud;
            }
            else if (pageType == typeof(Settings))
            {
                pageNavigationViewItem = Common.PageBase.NavItemPageSettings;
            }
            else if (pageType == typeof(Me))
            {
                pageNavigationViewItem = Common.PageBase.NavItemLogin;
            }
            else pageNavigationViewItem = Common.PageBase.NavItemBlank;
            return pageNavigationViewItem;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
