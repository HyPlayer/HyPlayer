using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public abstract class PlayBarValueConverter : IValueConverter
    {
        protected abstract object ExpandedValue { get; }

        protected abstract object CompactValue { get; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is true ? ExpandedValue : CompactValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
