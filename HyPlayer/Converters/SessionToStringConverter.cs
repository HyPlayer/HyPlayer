using LiteFM.Abstractions;
using System;
using Windows.UI.Xaml.Data;

namespace HyPlayer.Classes
{
    public partial class SessionToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if ((value is LastFMSession session) && session.HasLogined) return session.Name;
            else return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
