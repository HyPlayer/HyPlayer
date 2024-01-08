using HyPlayer.Pages;
using Microsoft.Toolkit.Uwp.UI.Converters;
using System;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;

namespace HyPlayer.Classes
{
    public class BooleanToWindowBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true && Common.Setting.CustomAcrylic is true)
            {
                var Brush = new Windows.UI.Xaml.Media.AcrylicBrush()
                {
                    BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
                    TintColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
                    TintOpacity = Common.Setting.CustomTintOpacity,
                    TintLuminosityOpacity = Common.Setting.CustomTintLuminosityOpacity,
                    FallbackColor = (Windows.UI.Color)Application.Current.Resources["SystemRevealAltHighColor"],
                };
                return Brush;
            }
            if (value is true && Common.Setting.CustomAcrylic is false) return Application.Current.Resources["NormalWindowBackgroundAcrylic"] as Brush;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class BooleanToBarPlayBarBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return Application.Current.Resources["SystemControlAcrylicWindowBrush"] as Brush;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class BooleanToGridPlayBarBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is false && Common.Setting.acrylicBackgroundStatus is true && Common.isExpanded is false) return Application.Current.Resources["GridPlayBarBackgroundAcrylic"] as Brush;
            if (value is false && Common.Setting.acrylicBackgroundStatus is false && Common.isExpanded is false) return Application.Current.Resources["SystemControlAcrylicElementMediumHighBrush"] as Brush;
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class ReversedBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return false;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class ReversedBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true) return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class EnumToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {

            if (value is Enum enumValue) return System.Convert.ToInt32(enumValue);
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }

    public class AlbumShadowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return Common.Setting.albumRound || Common.Setting.expandAlbumBreath
                ? 0
                : (double)Common.Setting.expandedCoverShadowDepth / 10;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class ImageUrlToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return new BitmapImage(new Uri(value.ToString() + "?param=70y70"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class PausedToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (parameter is true) return "重试";
            return value is true ? "继续" : "暂停";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class PlayBarImageRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is true ? new CornerRadius(4) : new CornerRadius(8, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class ThumbConverter : DependencyObject, IValueConverter
    {
        // Using a DependencyProperty as the backing store for SecondValue.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SecondValueProperty =
            DependencyProperty.Register("SecondValue", typeof(double), typeof(ThumbConverter),
                new PropertyMetadata(0d));

        public double SecondValue
        {
            get => (double)GetValue(SecondValueProperty);
            set => SetValue(SecondValueProperty, value);
        }


        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // assuming you want to display precentages

            return TimeSpan.FromMilliseconds(double.Parse(value.ToString())).ToString(@"hh\:mm\:ss");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class SongListSelectModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value != null)
                return (bool)value ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
            return SelectionMode.Single;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public class PlayBarMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is true ? new Thickness(16) : new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class PlayBarCornerRadiusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is true ? new CornerRadius(4) : new CornerRadius(8, 8, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class NullableColorToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {

            if (value is Color c)
            {
                return c;
            }
            else
            {
                return Colors.Transparent;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {

            return value is "" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
    public class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long || value is int)//时间戳
            {
                return FriendFormat(GetDateTimeFromTimeStamp((long)value));
            }
            else if (value is DateTime)//时间
            {
                return FriendFormat((DateTime)value);
            }
            else
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
        /// <summary>
        /// 时间戳转DateTime
        /// </summary>
        /// <param name="timestamp">时间戳(精确到毫秒)</param>
        /// <returns></returns>
        public static DateTime GetDateTimeFromTimeStamp(long timestamp)
        {
            return new DateTime(1970, 1, 1).AddTicks(timestamp * 10000);
        }
        /// <summary>
        /// 将DateTime转换为类似于“x分钟前”的格式
        /// </summary>
        /// <param name="dateTime">时间</param>
        /// <returns></returns>
        public static string FriendFormat(DateTime dateTime)
        {
            if (dateTime == null)
            {
                return string.Empty;
            }
            try
            {
                var now = DateTime.UtcNow.Ticks;
                var tick = dateTime.Ticks;
                var diff_ = now - tick;
                var diffDt = new DateTime(diff_);
                if (diffDt.Year <= 1 && diffDt.Month < 4)
                {
                    if (diffDt.Month <= 1)
                    {
                        if (diffDt.Day <= 1)
                        {
                            if (diffDt.Hour < 1)
                            {
                                if (diffDt.Minute < 2)
                                {
                                    return $"刚刚";
                                }
                                else
                                {
                                    return $"{diffDt.Minute}分钟前";
                                }
                            }
                            else
                            {
                                return $"{diffDt.Hour}小时前";
                            }
                        }
                        else
                        {
                            return $"{diffDt.Day}天前";
                        }
                    }
                    else
                    {
                        return $"{diffDt.Month - 1}个月前";
                    }
                }
                else
                {
                    return dateTime.ToString("yyyy/MM/dd");
                }
            }

            catch
            {
                return dateTime.ToString("yyyy/MM/dd");
            }



        }
    }
    public class PageToNavigationViewIndicatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            NavigationViewItem pageNavigationViewItem;
            if (value == null)
            {
                return Common.PageBase?.NavItemBlank;
            }
            Type pageType = value.GetType();
            if (pageType == typeof(Home))
                pageNavigationViewItem = Common.PageBase?.NavItemPageHome;
            else if (pageType == typeof(SongListDetail))
            {
                var displayedList = (SongListDetail)value;
                if (displayedList.playList == null) return Common.PageBase?.NavItemBlank;
                if (displayedList.playList.name == "每日歌曲推荐")
                    pageNavigationViewItem = Common.PageBase.NavItemDailyRcmd;
                else if (displayedList.playList.plid == Common.MySongLists[0].plid)
                    pageNavigationViewItem = Common.PageBase.NavItemsMyLovedPlaylist;
                else
                {
                    var item = Common.PageBase.NavItemsMyList.MenuItems.Where(t => (((NavigationViewItem)t)?.Tag as string) == $"Playlist{displayedList.playList.plid}").FirstOrDefault()
                        ?? Common.PageBase.NavItemsLikeList.MenuItems.Where(t => (((NavigationViewItem)t)?.Tag as string) == $"Playlist{displayedList.playList.plid}").FirstOrDefault();
                    if (item != null)
                    {
                        pageNavigationViewItem = (NavigationViewItem)item;
                    }
                    else
                    {
                        pageNavigationViewItem = Common.PageBase.NavItemBlank;
                    }
                }
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

    public class NegationBoolToVisibilityConverter : BoolToObjectConverter
    {
        public NegationBoolToVisibilityConverter()
        {
            base.TrueValue = Visibility.Visible;
            base.FalseValue = Visibility.Collapsed;
        }
    }

    public class TransparentColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, string language)
        {
            Color convert = (Color)value;
            return Color.FromArgb(0, convert.R, convert.G, convert.B);
        }

        public object ConvertBack(object value, Type targetType,
                                  object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
