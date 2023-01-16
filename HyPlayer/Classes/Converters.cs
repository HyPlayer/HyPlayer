using Opportunity.LrcParser;
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Classes
{
    public class BooleanToWindowBrushesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is true && Common.Setting.TintOpacityValue is true) return Application.Current.Resources["TransparentWindowBackgroundAcrylic"] as Brush;
            if (value is true && Common.Setting.TintOpacityValue is false) return Application.Current.Resources["NormalWindowBackgroundAcrylic"] as Brush;
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
            if (value is false && Common.Setting.acrylicBackgroundStatus is false && Common.isExpanded is false) return Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
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
            return value is true ? new CornerRadius(8) : new CornerRadius(8, 0, 0, 0);
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
            return value is true ? new CornerRadius(8) : new CornerRadius(8, 8, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long||value is int)//时间戳
            {
                return FriendFormat(GetDateTimeFromTimeStamp((long)value));
            }
            else if(value is DateTime)//时间
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
            return new DateTime(1970,1,1).AddTicks(timestamp * 10000);
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
                var now = DateTime.Now.Ticks;
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
}
