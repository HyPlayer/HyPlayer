using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Converters;

public partial class SongListSelectModeConverter : IValueConverter
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