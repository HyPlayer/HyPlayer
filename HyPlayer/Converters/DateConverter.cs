using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class DateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long || value is int)
            {
                return FriendFormat(GetDateTimeFromTimeStamp((long)value));
            }
            else if (value is DateTime time)
            {
                return FriendFormat(time);
            }
            else
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;

        public static DateTime GetDateTimeFromTimeStamp(long timestamp)
        {
            return new DateTime(1970, 1, 1).AddTicks(timestamp * 10000);
        }

        public static string FriendFormat(DateTime dateTime)
        {
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
}
